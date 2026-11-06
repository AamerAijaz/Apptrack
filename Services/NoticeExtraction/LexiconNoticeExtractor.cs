using System.Globalization;
using System.Text.RegularExpressions;

namespace Apptrack.Services.NoticeExtraction;

/// <summary>
/// Reads one application notice from message content with a closed phrase list.
/// A message outside that list is not a notice. This reader never calls a language model.
/// </summary>
public sealed partial class LexiconNoticeExtractor : IApplicationNoticeExtractor
{
    private static readonly string[] Acknowledgements =
    [
        "thanks for applying",
        "thank you for applying",
        "application received",
        "we received your application",
        "under review",
    ];

    private static readonly string[] InterviewingPhrases =
    [
        "interview",
        "interviewing",
        "phone screen",
        "take-home",
        "assessment",
    ];

    private static readonly string[] RejectedPhrases =
    [
        "other candidates",
        "not moving forward",
        "will not be progressing",
    ];

    private static readonly string[] AbstainPhrases =
    [
        "offer",
        "newsletter",
        "verify your email",
        "job alert",
        "on hold",
        "withdrawn",
    ];

    private static readonly string[] CompanyLeadIns = ["the", "a", "an", "our", "your"];
    private static readonly string[] IgnoredTitles = ["the", "a", "an", "open"];

    private static readonly string[] ListedPhrases =
        Acknowledgements.Concat(InterviewingPhrases).Concat(RejectedPhrases).Concat(AbstainPhrases).ToArray();

    public Task<ApplicationNotice?> ExtractAsync(string messageId, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Read(content));
    }

    private static ApplicationNotice? Read(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        if (!TryReadSentAt(content, out var sentAt))
            return null;

        Split(content, out var from, out var subject, out var body);
        var subjectText = WithoutAddresses(subject);
        var bodyText = WithoutAddresses(body);
        if (AbstainPhrases.Any(phrase => HasPhrase(subjectText, phrase) || HasPhrase(bodyText, phrase)))
            return null;
        if (!TryStatus(subjectText, bodyText, out var status))
            return null;

        var grounded = Collapse(WithoutAddresses(content));
        if (!TryCompany(from, subjectText, bodyText, grounded, out var company))
            return null;
        if (!TryRole(subjectText, bodyText, out var role))
            return null;

        return new ApplicationNotice(company, role, status, sentAt);
    }

    private static bool TryStatus(string subject, string body, out ApplicationStatus status)
    {
        var interviewing = InterviewingPhrases.Any(phrase => HasPhrase(subject, phrase) || HasPhrase(body, phrase));
        var rejected = RejectedPhrases.Any(phrase => HasPhrase(subject, phrase) || HasPhrase(body, phrase));
        var acknowledged = Acknowledgements.Any(phrase => HasPhrase(subject, phrase) || HasPhrase(body, phrase));

        if (interviewing && rejected)
        {
            status = default;
            return false;
        }

        if (rejected)
            status = ApplicationStatus.Rejected;
        else if (interviewing)
            status = ApplicationStatus.Interviewing;
        else if (acknowledged)
            status = ApplicationStatus.ApplicationSent;
        else
        {
            status = default;
            return false;
        }

        return true;
    }

    private static bool TryCompany(
        string from,
        string subject,
        string body,
        string grounded,
        out string company)
    {
        company = "";
        var displayName = DisplayName(from);
        if (displayName is not null)
        {
            if (!Appears(displayName, grounded))
                return false;
            company = displayName;
            return true;
        }

        var spans = CompanySpans(subject).Concat(CompanySpans(body)).ToList();
        var chosen = ChooseCompany(spans);
        if (chosen is null || !Appears(chosen, grounded))
            return false;

        company = chosen;
        return true;
    }

    private static string? DisplayName(string from)
    {
        var trimmed = from.Trim();
        if (trimmed.Length == 0)
            return null;

        var angle = trimmed.IndexOf('<');
        var name = angle >= 0 ? trimmed[..angle] : trimmed;
        name = name.Trim().Trim('"');
        if (name.Length == 0)
            return null;
        if (angle < 0 && EmailAddress().IsMatch(name))
            return null;
        return name;
    }

    private static IEnumerable<string> CompanySpans(string text)
    {
        foreach (Match match in CompanySpanPattern().Matches(text))
        {
            var words = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count > 0 && CompanyLeadIns.Contains(words[0], StringComparer.OrdinalIgnoreCase))
                words.RemoveAt(0);
            if (words.Count == 0)
                continue;

            var span = string.Join(' ', words);
            if (ListedPhrases.Any(phrase => span.Equals(phrase, StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return span;
        }
    }

    private static string? ChooseCompany(IReadOnlyList<string> spans)
    {
        var ordered = spans
            .Select((span, index) => (Span: Collapse(span), index))
            .Where(item => item.Span.Length > 0)
            .OrderByDescending(item => item.Span.Length)
            .ThenBy(item => item.index)
            .ToList();

        var kept = new List<string>();
        foreach (var (span, _) in ordered)
        {
            if (kept.Any(existing => SameCompany(existing, span)))
                continue;
            kept.Add(span);
        }

        return kept.Count == 1 ? kept[0] : null;
    }

    private static bool SameCompany(string left, string right)
    {
        var longer = left.Length >= right.Length ? left : right;
        var shorter = left.Length >= right.Length ? right : left;
        return longer.Equals(shorter, StringComparison.OrdinalIgnoreCase)
            || longer.StartsWith(shorter + " ", StringComparison.OrdinalIgnoreCase)
            || longer.EndsWith(" " + shorter, StringComparison.OrdinalIgnoreCase)
            || longer.Contains(" " + shorter + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRole(string subject, string body, out ApplicationRole role)
    {
        role = ApplicationRole.Unnamed;
        var titles = Titles(subject).Concat(Titles(body))
            .Select(Collapse)
            .Where(title => title.Length > 0 && !IgnoredTitles.Contains(title, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (titles.Any(title => AndWord().IsMatch(title)))
            return false;

        var distinct = titles
            .Select(title => title.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinct.Count > 1)
            return false;
        if (distinct.Count == 1)
            role = ApplicationRole.Named(titles[0]);
        return true;
    }

    private static IEnumerable<string> Titles(string text)
    {
        foreach (var pattern in RolePatterns())
        {
            foreach (Match match in pattern.Matches(text))
                yield return match.Groups[1].Value;
        }
    }

    private static bool HasPhrase(string text, string phrase)
    {
        var pattern = @"\b" + string.Join(@"\s+", phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @"\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool Appears(string company, string grounded)
    {
        var normalized = Collapse(company);
        return normalized.Length > 0
            && grounded.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSentAt(string content, out DateTimeOffset sentAt)
    {
        sentAt = default;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                return false;
            if (!line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = line["Date:".Length..].Trim();
            return DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out sentAt);
        }

        return false;
    }

    private static void Split(string content, out string from, out string subject, out string body)
    {
        from = "";
        subject = "";
        var bodyLines = new List<string>();
        var inBody = false;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (!inBody)
            {
                if (line.Length == 0)
                {
                    inBody = true;
                    continue;
                }

                if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase) && from.Length == 0)
                    from = line["From:".Length..].Trim();
                else if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) && subject.Length == 0)
                    subject = line["Subject:".Length..].Trim();
                continue;
            }

            bodyLines.Add(line);
        }

        body = string.Join('\n', bodyLines);
    }

    private static string WithoutAddresses(string value) => EmailAddress().Replace(value, " ");

    private static string Collapse(string value) => Whitespace().Replace(value, " ").Trim();

    private static IEnumerable<Regex> RolePatterns()
    {
        yield return PositionOf();
        yield return PositionLabel();
        yield return RoleOf();
        yield return RoleLabel();
        yield return ApplyingFor();
        yield return ApplicationFor();
        yield return InterviewFor();
        yield return TitlePosition();
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\band\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AndWord();

    [GeneratedRegex(@"\b(?:at|from)\s+((?:[^\s.!?\n]+\s+){0,5}[^\s.!?\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompanySpanPattern();

    private const string Title = @"(?:(?!(?:at|with|in)\b)[A-Za-z0-9][A-Za-z0-9'&+-]*)(?:\s+(?!(?:at|with|in)\b)[A-Za-z0-9][A-Za-z0-9'&+-]*){0,5}";

    [GeneratedRegex($@"position of\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionOf();

    [GeneratedRegex($@"position:\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionLabel();

    [GeneratedRegex($@"role of\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleOf();

    [GeneratedRegex($@"role:\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleLabel();

    [GeneratedRegex($@"applying for\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplyingFor();

    [GeneratedRegex($@"application for\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationFor();

    [GeneratedRegex($@"interview for\s+({Title})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InterviewFor();

    [GeneratedRegex($@"({Title})\s+position\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitlePosition();
}
