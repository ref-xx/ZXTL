namespace ZXTL
{
    public static class TraceLogOrderParser
    {
        private static readonly Dictionary<string, TraceLogOrderField> KeywordMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ADDRESS"] = TraceLogOrderField.Address,
                ["CYCLES"] = TraceLogOrderField.Cycle,
                ["DISASSEMBLY"] = TraceLogOrderField.Disassembly,
                ["MARKERS"] = TraceLogOrderField.Event,
                ["MEM4"] = TraceLogOrderField.OpcodeValue,
                ["ASCII4"] = TraceLogOrderField.OpcodeAscii
            };

        private static readonly Dictionary<string, string[]> MacroMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["BASEREGS"] =
                [
                    "RA", "RF", "RBC", "RDE", "RHL",
                    "RIX", "RIY", "RSP", "RPC", "RWZ", "RIR"
                ],
                ["EXREGS"] =
                [
                    "RAFx", "RBCx", "RDEx", "RHLx"
                ],
                ["INTREGS"] =
                [
                    "RIM", "RIFF1", "RIFF2"
                ]
            };

        private static readonly Dictionary<string, TraceLogOrderField> RegisterMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PC"] = TraceLogOrderField.PC,
                ["SP"] = TraceLogOrderField.SP,
                ["A"] = TraceLogOrderField.A,
                ["F"] = TraceLogOrderField.F,
                ["B"] = TraceLogOrderField.B,
                ["C"] = TraceLogOrderField.C,
                ["D"] = TraceLogOrderField.D,
                ["E"] = TraceLogOrderField.E,
                ["H"] = TraceLogOrderField.H,
                ["L"] = TraceLogOrderField.L,
                ["AF"] = TraceLogOrderField.AF,
                ["BC"] = TraceLogOrderField.BC,
                ["DE"] = TraceLogOrderField.DE,
                ["HL"] = TraceLogOrderField.HL,
                ["IX"] = TraceLogOrderField.IX,
                ["IY"] = TraceLogOrderField.IY,
                ["IR"] = TraceLogOrderField.IR,
                ["AFX"] = TraceLogOrderField.AFx,
                ["BCX"] = TraceLogOrderField.BCx,
                ["DEX"] = TraceLogOrderField.DEx,
                ["HLX"] = TraceLogOrderField.HLx,
                ["WZ"] = TraceLogOrderField.WZ,
                ["IM"] = TraceLogOrderField.IM,
                ["IFF1"] = TraceLogOrderField.IFF1,
                ["IFF2"] = TraceLogOrderField.IFF2
            };

        public static TraceLogOrderDefinition Parse(string orderText)
        {
            TraceLogOrderDefinition definition = new();
            ParseInto(definition, orderText);
            return definition;
        }

        public static void ParseInto(TraceLogOrderDefinition definition, string? orderText)
        {
            ArgumentNullException.ThrowIfNull(definition);

            definition.Clear();
            definition.RawText = orderText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(orderText))
            {
                return;
            }

            foreach (string token in Tokenize(orderText))
            {
                ParseToken(definition, token);
            }
        }

        public static string Describe(TraceLogOrderFieldSpec item)
        {
            ArgumentNullException.ThrowIfNull(item);

            string source = item.ExpandedFromToken is null
                ? item.RawToken
                : $"{item.RawToken} <= {item.ExpandedFromToken}";
            string width = item.FixedWidth is int fixedWidth ? $" width={fixedWidth}" : string.Empty;

            return item.Kind switch
            {
                TraceLogOrderItemKind.Field =>
                    $"Field:{item.Field}{width} [{source}]",

                TraceLogOrderItemKind.Register =>
                    $"Register:{item.Field}{width} [{source}]",

                TraceLogOrderItemKind.FormatDirective =>
                    $"Directive:{item.FormatTarget}={item.ValueFormat}{width} [{source}]",

                _ =>
                    $"Unknown{width} [{source}]"
            };
        }

        private static IEnumerable<string> Tokenize(string orderText)
        {
            foreach (string token in orderText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return token.Trim();
            }
        }

        private static void ParseToken(TraceLogOrderDefinition definition, string token)
        {
            if (token.Length == 0)
            {
                return;
            }

            SplitLengthSuffix(token, out string normalizedToken, out int? fixedWidth);

            if (TryExpandMacro(definition, token, normalizedToken, fixedWidth))
            {
                return;
            }

            if (TryParseFormatDirective(normalizedToken, out TraceLogOrderFormatTarget formatTarget, out TraceLogOrderValueFormat valueFormat))
            {
                definition.Items.Add(new TraceLogOrderFieldSpec
                {
                    Kind = TraceLogOrderItemKind.FormatDirective,
                    RawToken = token,
                    NormalizedToken = normalizedToken,
                    FixedWidth = fixedWidth,
                    FormatTarget = formatTarget,
                    ValueFormat = valueFormat
                });
                return;
            }

            if (KeywordMap.TryGetValue(normalizedToken, out TraceLogOrderField keywordField))
            {
                definition.Items.Add(new TraceLogOrderFieldSpec
                {
                    Kind = TraceLogOrderItemKind.Field,
                    RawToken = token,
                    NormalizedToken = normalizedToken,
                    FixedWidth = fixedWidth,
                    Field = keywordField
                });
                return;
            }

            if (TryParseRegisterToken(normalizedToken, out TraceLogOrderField registerField))
            {
                definition.Items.Add(new TraceLogOrderFieldSpec
                {
                    Kind = TraceLogOrderItemKind.Register,
                    RawToken = token,
                    NormalizedToken = normalizedToken,
                    FixedWidth = fixedWidth,
                    Field = registerField
                });
                return;
            }

            definition.Items.Add(new TraceLogOrderFieldSpec
            {
                Kind = TraceLogOrderItemKind.Unknown,
                RawToken = token,
                NormalizedToken = normalizedToken,
                FixedWidth = fixedWidth,
                Field = TraceLogOrderField.Unknown
            });
        }

        private static bool TryExpandMacro(
            TraceLogOrderDefinition definition,
            string rawToken,
            string normalizedToken,
            int? fixedWidth)
        {
            if (!MacroMap.TryGetValue(normalizedToken, out string[]? expandedTokens))
            {
                return false;
            }

            foreach (string expanded in expandedTokens)
            {
                if (TryParseRegisterToken(expanded, out TraceLogOrderField registerField))
                {
                    definition.Items.Add(new TraceLogOrderFieldSpec
                    {
                        Kind = TraceLogOrderItemKind.Register,
                        RawToken = expanded,
                        NormalizedToken = expanded,
                        FixedWidth = fixedWidth,
                        Field = registerField,
                        ExpandedFromToken = rawToken
                    });
                }
                else
                {
                    definition.Items.Add(new TraceLogOrderFieldSpec
                    {
                        Kind = TraceLogOrderItemKind.Unknown,
                        RawToken = expanded,
                        NormalizedToken = expanded,
                        FixedWidth = fixedWidth,
                        ExpandedFromToken = rawToken
                    });
                }
            }

            return true;
        }

        private static void SplitLengthSuffix(string token, out string normalizedToken, out int? fixedWidth)
        {
            normalizedToken = token;
            fixedWidth = null;

            int hashIndex = token.LastIndexOf('#');
            if (hashIndex <= 0 || hashIndex == token.Length - 1)
            {
                return;
            }

            ReadOnlySpan<char> widthSpan = token.AsSpan(hashIndex + 1);
            for (int i = 0; i < widthSpan.Length; i++)
            {
                if (!char.IsAsciiDigit(widthSpan[i]))
                {
                    return;
                }
            }

            if (!int.TryParse(widthSpan, out int parsedWidth))
            {
                return;
            }

            normalizedToken = token[..hashIndex];
            fixedWidth = parsedWidth;
        }

        private static bool TryParseRegisterToken(string token, out TraceLogOrderField field)
        {
            field = TraceLogOrderField.Unknown;

            if (token.Length < 2 || token[0] != 'R' && token[0] != 'r')
            {
                return false;
            }

            string registerName = token[1..];
            return RegisterMap.TryGetValue(registerName, out field);
        }

        private static bool TryParseFormatDirective(
            string token,
            out TraceLogOrderFormatTarget target,
            out TraceLogOrderValueFormat format)
        {
            target = TraceLogOrderFormatTarget.None;
            format = TraceLogOrderValueFormat.None;

            if (token.Length != 3 || token[1] != '=')
            {
                return false;
            }

            char left = char.ToUpperInvariant(token[0]);
            char right = char.ToUpperInvariant(token[2]);

            target = left switch
            {
                'R' => TraceLogOrderFormatTarget.Registers,
                '$' => TraceLogOrderFormatTarget.DollarValue,
                _ => TraceLogOrderFormatTarget.None
            };

            if (target == TraceLogOrderFormatTarget.None)
            {
                return false;
            }

            format = right switch
            {
                'H' => TraceLogOrderValueFormat.Hex,
                'D' => TraceLogOrderValueFormat.Decimal,
                'S' when target == TraceLogOrderFormatTarget.DollarValue => TraceLogOrderValueFormat.String,
                _ => TraceLogOrderValueFormat.None
            };

            return format != TraceLogOrderValueFormat.None;
        }
    }
}
