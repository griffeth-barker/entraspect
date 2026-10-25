using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>What one run of a check found, and the period of logs it read if it reads one.</summary>
public sealed record CheckRun(IReadOnlyList<Finding> Findings, DataPeriod? DataPeriod = null);

/// <summary>
/// A period of log data a check read, for example sign-ins, and how many entries it read, so its page can say what the
/// findings are based on. <paramref name="ItemName"/> is the singular name of an entry, for example "sign-in".
/// </summary>
public sealed record DataPeriod(DateTimeOffset Start, DateTimeOffset End, int ItemsRead, string ItemName);
