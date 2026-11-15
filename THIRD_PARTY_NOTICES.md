# 第三方依赖与资源来源

本文记录 ArkSide 使用的主要组件与资源来源，随应用包分发。ArkSide 目前没有添加项目级 `LICENSE`；本文不为项目代码或第三方素材重新授予许可。第三方代码、模型、运行时和素材分别适用其原有条款。

## 项目资源

| 资源 | 来源 |
| --- | --- |
| `ArkSide.png` 及由它生成的 `assets/ArkSide.ico`、`assets/ArkSide.icns` | 项目作者自行制作 |
| `干员数据/干员详细信息.json`、`干员数据/干员禁用规则.json` | 项目作者自行编写和整理 |
| `干员数据/干员头像/*.png` | 项目作者从 [PRTS Wiki](https://prts.wiki/) 获取 |
| `干员数据/识别测试/`、`windowsTest/` 中的游戏截图 | 用于识别回归与问题复现的游戏画面；相关游戏内容权利归各自权利人 |

明日方舟角色名称、头像和游戏画面等内容的权利归原权利人。PRTS 是资源获取来源；其站点条款见 [PRTS：版权](https://prts.wiki/w/PRTS:%E7%89%88%E6%9D%83)。本文未将整站条款推定为每张游戏图片的单独授权。应用运行不访问 PRTS。

## .NET 与桌面组件

以下版本来自 `Directory.Packages.props`，许可标识来自本次使用的 NuGet 包元数据。完整依赖树见对应的 `packages*.lock.json` 和应用包内的 `ArkSide.deps.json`。

| 组件 | 版本 | 包声明许可 | 上游 |
| --- | --- | --- | --- |
| Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent | 12.1.3 | MIT | [Avalonia](https://github.com/AvaloniaUI/Avalonia) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) |
| Microsoft.Extensions.Hosting、Microsoft.Extensions.Logging.Console | 10.0.12 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| Maa.Framework.Native 及其 C# Binding 依赖 | 5.10.0 | LGPL-3.0-only | [MaaFramework.Binding.CSharp](https://github.com/MaaXYZ/MaaFramework.Binding.CSharp/tree/27c69a5b8ff41b6002ead71f403a16f442a7168e) |
| Maa.Framework.Runtime.osx-arm64、Maa.Framework.Runtime.win-x64 | 5.10.5 | LGPL-3.0-only | [MaaFramework](https://github.com/MaaXYZ/MaaFramework/tree/13ff95cdfa2b20fa6bdd361779278115bc03c0b4) |
| .NET 自包含运行时 | net10.0，实际版本见 `ArkSide.deps.json` | MIT 及所含第三方组件条款 | [.NET 许可](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[第三方声明](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) |

MaaFramework 的 DLL / dylib 以独立动态库随包分发，ArkSide 未修改其上游源码。上表链接指向当前 NuGet 包记录的源码提交；相关许可文本见 [LGPL-3.0](https://www.gnu.org/licenses/lgpl-3.0.html) 和 [GPL-3.0](https://www.gnu.org/licenses/gpl-3.0.html)。包内的上游许可文件和声明应保留。

## Python、离线识别与 OCR 模型

依赖版本来自 `tools/offline_recognition/requirements-core.txt` 和 `requirements-build.txt`；以下许可信息依据安装包元数据整理，具体以对应发行包的完整许可和第三方声明为准。

| 组件 | 版本 | 包声明许可 | 上游 |
| --- | --- | --- | --- |
| Python | 构建使用 3.12；补丁版本由构建环境决定 | PSF 及所含组件条款 | [Python](https://www.python.org/psf/license/) |
| RapidOCR / rapidocr-onnxruntime | 1.4.4 | Apache-2.0 | [RapidOCR](https://github.com/RapidAI/RapidOCR) |
| ONNX Runtime | 1.31.1 | MIT | [ONNX Runtime](https://github.com/microsoft/onnxruntime) |
| OpenCV / opencv-python | 5.0.0.93 | Apache-2.0 及所含第三方条款 | [opencv-python](https://github.com/opencv/opencv-python) |
| NumPy | 2.5.4 | BSD-3-Clause、0BSD、MIT、Zlib、CC0-1.0 | [NumPy](https://github.com/numpy/numpy) |
| Pillow | 13.0.0 | MIT-CMU | [Pillow](https://github.com/python-pillow/Pillow) |
| flatbuffers | 25.12.19 | Apache-2.0 | [FlatBuffers](https://github.com/google/flatbuffers) |
| packaging | 26.3 | Apache-2.0 OR BSD-2-Clause | [packaging](https://github.com/pypa/packaging) |
| protobuf | 7.36.2 | BSD-3-Clause | [Protocol Buffers](https://github.com/protocolbuffers/protobuf) |
| pyclipper | 1.4.0 | MIT | [pyclipper](https://github.com/fonttools/pyclipper) |
| PyYAML | 6.0.3 | MIT | [PyYAML](https://github.com/yaml/pyyaml) |
| shapely | 2.2.0 | BSD-3-Clause 及所含 GEOS 条款 | [Shapely](https://github.com/shapely/shapely) |
| six | 1.17.0 | MIT | [six](https://github.com/benjaminp/six) |
| tqdm | 4.70.1 | MPL-2.0 AND MIT | [tqdm](https://github.com/tqdm/tqdm) |
| PyInstaller（构建工具及随包 bootloader） | 6.22.3 | GPLv2-or-later，附打包程序分发例外；以其原文为准 | [PyInstaller](https://github.com/pyinstaller/pyinstaller/blob/develop/COPYING.txt) |

RapidOCR 安装包自带以下模型；ArkSide 在本机读取它们，不在运行时下载模型：

- `ch_PP-OCRv4_det_infer.onnx`
- `ch_ppocr_mobile_v2.0_cls_infer.onnx`
- `ch_PP-OCRv4_rec_infer.onnx`

模型随 RapidOCR 发行包提供，来源关联 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)；具体模型信息与哈希见 [原始识别证据](https://github.com/xiaogewzw/ArkSide/blob/main/tools/offline_recognition/verification/v2/original-evidence.json)。PySide6 只用于独立开发验证界面，不进入正式应用包。

正式包保留已收集的 Python 发行包元数据与许可文件，可在 `recognition/worker/_internal` 下查阅（macOS 对应 `Contents/Resources/recognition/worker/_internal`）。
