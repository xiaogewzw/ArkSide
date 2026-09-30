import AppKit
import CoreGraphics
import ScreenCaptureKit
import UniformTypeIdentifiers

private func cString(_ value: String) -> UnsafeMutablePointer<CChar>? { strdup(value) }

@_cdecl("GSFree")
public func gsFree(_ pointer: UnsafeMutableRawPointer?) { free(pointer) }

private func windowInfo() -> [[String: Any]] {
    let items = CGWindowListCopyWindowInfo([.optionAll], kCGNullWindowID) as? [[String: Any]] ?? []
    var displays = [CGDirectDisplayID](repeating: 0, count: 16)
    var displayCount: UInt32 = 0
    _ = CGGetActiveDisplayList(UInt32(displays.count), &displays, &displayCount)
    let mainTop = NSScreen.main?.frame.maxY ?? 0
    return items.compactMap { item in
        let id = item[kCGWindowNumber as String] as? UInt32 ?? 0
        let pid = item[kCGWindowOwnerPID as String] as? Int32 ?? 0
        let layer = item[kCGWindowLayer as String] as? Int ?? -1
        let bounds = item[kCGWindowBounds as String] as? [String: CGFloat] ?? [:]
        let app = NSRunningApplication(processIdentifier: pid)
        let bundlePath = app?.bundleURL?.path
        let width = bounds["Width"] ?? 0
        let height = bounds["Height"] ?? 0
        guard id != 0, pid > 0, layer == 0, width > 0, height > 0 else { return nil }
        let center = CGPoint(x: (bounds["X"] ?? 0) + width / 2,
                             y: (bounds["Y"] ?? 0) + height / 2)
        let displayId = displays.prefix(Int(displayCount)).first(where: { CGDisplayBounds($0).contains(center) })
        let displayBounds = displayId.map { CGDisplayBounds($0) }
        let scale = displayId.flatMap { displayBounds?.width == 0 ? nil : Double(CGDisplayPixelsWide($0)) / Double(displayBounds!.width) }
        let screen = NSScreen.screens.first { screen in
            guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { return false }
            return number.uint32Value == displayId
        }
        let visible = screen?.visibleFrame
        return ["id": id, "pid": pid,
            "title": item[kCGWindowName as String] as? String ?? "",
            "owner": item[kCGWindowOwnerName as String] as? String ?? "",
            "bundleId": app?.bundleIdentifier ?? "", "bundlePath": bundlePath ?? "",
            "x": bounds["X"] ?? 0, "y": bounds["Y"] ?? 0,
            "width": width, "height": height,
            "displayId": displayId.map { String($0) } ?? "",
            "pixelsPerPoint": scale ?? 0,
            "workX": visible?.minX ?? 0, "workY": visible.map { mainTop - $0.maxY } ?? 0,
            "workWidth": visible?.width ?? 0, "workHeight": visible?.height ?? 0,
            "foreground": NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
            "visible": item[kCGWindowIsOnscreen as String] as? Bool ?? false,
            "minimized": app?.isHidden ?? false,
            "self": pid == getpid()]
    }
}

@_cdecl("GSWindowsJSON")
public func gsWindowsJSON() -> UnsafeMutablePointer<CChar>? {
    guard let data = try? JSONSerialization.data(withJSONObject: windowInfo()),
          let text = String(data: data, encoding: .utf8) else { return cString("[]") }
    return cString(text)
}

@_cdecl("GSBundleId")
public func gsBundleId(_ path: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    guard let path, let bundle = Bundle(url: URL(fileURLWithPath: String(cString: path))),
          let id = bundle.bundleIdentifier else { return nil }
    return cString(id)
}

@_cdecl("GSChooseApplication")
public func gsChooseApplication() -> UnsafeMutablePointer<CChar>? {
    // Avalonia's file picker drops .app directories from its IStorageFile result.
    // Keep application packages opaque so users never have to browse inside them.
    guard Thread.isMainThread else { return nil }
    let panel = NSOpenPanel()
    panel.title = "选择游戏 .app"
    panel.canChooseFiles = true
    panel.canChooseDirectories = false
    panel.allowsMultipleSelection = false
    panel.treatsFilePackagesAsDirectories = false
    panel.allowedContentTypes = [.applicationBundle]
    guard panel.runModal() == .OK, let url = panel.url else { return nil }
    return cString(url.path)
}

@_cdecl("GSScreenPermission")
public func gsScreenPermission(_ request: Bool) -> Bool {
    return request ? CGRequestScreenCaptureAccess() : CGPreflightScreenCaptureAccess()
}

@_cdecl("GSMouseX")
public func gsMouseX() -> Double { NSEvent.mouseLocation.x }

@_cdecl("GSMouseY")
public func gsMouseY() -> Double {
    (NSScreen.main?.frame.maxY ?? 0) - NSEvent.mouseLocation.y
}

@_cdecl("GSCapturePNG")
public func gsCapturePNG(_ windowId: UInt32, _ byteCount: UnsafeMutablePointer<Int32>?,
                         _ errorText: UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutableRawPointer? {
    byteCount?.pointee = 0
    errorText?.pointee = nil
    guard CGPreflightScreenCaptureAccess() else {
        errorText?.pointee = cString("缺少屏幕录制权限；请在系统设置授权后重试")
        return nil
    }
    guard #available(macOS 14.0, *) else {
        errorText?.pointee = cString("ScreenCaptureKit 需要 macOS 14 或更新版本")
        return nil
    }
    let semaphore = DispatchSemaphore(value: 0)
    var output: Data?
    var failure: String?
    Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            guard let window = content.windows.first(where: { $0.windowID == windowId }) else {
                throw NSError(domain: "GameSidebar", code: 1, userInfo: [NSLocalizedDescriptionKey: "窗口已消失或不可捕获"])
            }
            let filter = SCContentFilter(desktopIndependentWindow: window)
            let config = SCStreamConfiguration()
            let scale = windowInfo().first(where: { ($0["id"] as? UInt32) == windowId })?["pixelsPerPoint"] as? Double ?? 1
            config.width = max(1, Int(window.frame.width * max(1, scale)))
            config.height = max(1, Int(window.frame.height * max(1, scale)))
            config.showsCursor = true
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
            output = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])
        } catch { failure = error.localizedDescription }
        semaphore.signal()
    }
    if semaphore.wait(timeout: .now() + 10) == .timedOut {
        errorText?.pointee = cString("ScreenCaptureKit 截图超时")
        return nil
    }
    guard let output, !output.isEmpty else {
        errorText?.pointee = cString(failure ?? "截图没有返回图像")
        return nil
    }
    let pointer = malloc(output.count)!
    output.copyBytes(to: pointer.assumingMemoryBound(to: UInt8.self), count: output.count)
    byteCount?.pointee = Int32(output.count)
    return pointer
}
