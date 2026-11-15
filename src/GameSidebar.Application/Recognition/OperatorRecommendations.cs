namespace GameSidebar.Application.Recognition;

public sealed record TaggedOperator(string Id, string Name, string Portrait,
    IReadOnlyList<string> MainCovenant, IReadOnlyList<string> ViceCovenant, IReadOnlyList<string> Tag)
{
    public int? Rank { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TagCovenants { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public bool MatchesViceCovenant(string? viceCovenant) =>
        viceCovenant is null || ViceCovenant.Contains(viceCovenant);

    public IReadOnlyList<string> DisplayCovenants(string tag) =>
        TagCovenants.TryGetValue(tag, out var scope) ? scope : ViceCovenant;
}

public sealed record OperatorRecommendationGroup(string Tag, int AvailableCount, int DisabledCount,
    IReadOnlyList<TaggedOperator> Operators)
{
    public string EmptyMessage => DisabledCount > 0 ? "当前筛选条件下的干员均被禁用" : "当前筛选条件下暂无可用干员";
}

public static class OperatorRecommendations
{
    public static IReadOnlyList<OperatorRecommendationGroup> Select(
        IReadOnlyList<TaggedOperator> operators, OperatorRecognitionResult result,
        string? tag = null, string? viceCovenant = null, int limit = int.MaxValue, int? rank = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (rank is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(rank));
        var disabled = result.DisabledOperators.ToHashSet(StringComparer.Ordinal);
        if (operators.Count != result.TotalOperators || disabled.Count != result.DisabledOperators.Count ||
            operators.Count - disabled.Count != result.AvailableOperators ||
            disabled.Except(operators.Select(o => o.Name), StringComparer.Ordinal).Any())
            throw new InvalidDataException("识别结果与干员库不一致，请重新导入截图。");
        var tags = operators.SelectMany(o => o.Tag).Distinct(StringComparer.Ordinal)
            .Where(t => tag is null || t == tag);
        return tags.Select(t =>
        {
            var matching = operators.Where(o => o.Tag.Contains(t) && (rank is null || o.Rank == rank) &&
                o.MatchesViceCovenant(viceCovenant)).ToArray();
            var available = matching.Where(o => !disabled.Contains(o.Name)).ToArray();
            return new OperatorRecommendationGroup(t, available.Length, matching.Length - available.Length,
                available.Take(limit).ToArray());
        }).ToArray();
    }
}
