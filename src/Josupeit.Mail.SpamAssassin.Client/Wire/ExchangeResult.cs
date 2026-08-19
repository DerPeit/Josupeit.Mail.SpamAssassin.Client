// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// What one exchange produced, before it is turned into the type a caller asked for.
internal readonly record struct ExchangeResult(ResponseHead Head, string? Text, long BodyLength);
