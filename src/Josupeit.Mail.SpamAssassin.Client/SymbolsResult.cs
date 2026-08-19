// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Collections.Generic;
using System.Linq;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The answer to a symbols request: the verdict, plus the names of the rules that produced it.
/// </summary>
/// <remarks>
/// Two of these are equal when the verdicts match and the same rules were listed in the same order. That takes
/// hand-written equality rather than the record's own: a record compares its members, and a member holding a list
/// compares by identity, so two results from two identical scans would otherwise be unequal.
/// </remarks>
/// <param name="Verdict">Whether the message counts as spam, with the score and the threshold.</param>
/// <param name="Symbols">
/// The rules that fired, in the order the daemon listed them, and empty when none did. The names are the rule
/// identifiers from SpamAssassin's configuration - <c>BAYES_99</c>, <c>GTUBE</c>, and so on - which means they are only
/// as stable as the rule set the daemon runs.
/// </param>
public sealed record SymbolsResult(SpamVerdict Verdict, IReadOnlyList<string> Symbols)
{
    /// <summary>
    /// Compares two results by their verdicts and by the rules they list, in order.
    /// </summary>
    /// <param name="other">The result to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when both hold the same verdict and the same rule names in the same order.</returns>
    public bool Equals(SymbolsResult? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Verdict == other.Verdict && Symbols.SequenceEqual(other.Symbols);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Verdict);

        foreach (var symbol in Symbols)
            hash.Add(symbol);

        return hash.ToHashCode();
    }
}
