using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Persistence.CustomControls;

internal static class CustomSignalFormulaEngine
{
    public static bool TryEvaluateBooleanExpression(string? formula, IReadOnlyDictionary<string, object?> variables, out bool value, out string errorMessage)
    {
        value = false;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(formula))
        {
            errorMessage = "Condition is required.";
            return false;
        }

        try
        {
            var parser = new Parser(formula, variables);
            var result = parser.ParseExpression();
            parser.EnsureCompleted();
            if (result is not bool booleanResult)
            {
                errorMessage = "Condition must evaluate to bool.";
                return false;
            }

            value = booleanResult;
            return true;
        }
        catch (FormulaException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    public static bool TryEvaluate(CustomSignalDefinition definition, Func<string, object?> resolveVariable, out object? value, out string errorMessage)
    {
        value = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(definition.Formula))
        {
            errorMessage = "Formula is required.";
            return false;
        }

        var variables = definition.Variables
            .Where(static variable => variable is not null && !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => resolveVariable(group.First().Name), StringComparer.OrdinalIgnoreCase);

        try
        {
            var parser = new Parser(definition.Formula, variables);
            value = parser.ParseExpression();
            parser.EnsureCompleted();
            value = CustomSignalRuntimeHelper.ConvertToDataType(value, definition.DataType);
            return true;
        }
        catch (FormulaException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    public static bool TryValidate(CustomSignalDefinition definition, out string errorMessage)
    {
        return TryEvaluate(definition, _ => definition.DataType switch
        {
            CustomSignalDataType.Boolean => false,
            CustomSignalDataType.Text => string.Empty,
            _ => 0d
        }, out _, out errorMessage);
    }

    private sealed class Parser
    {
        private readonly IReadOnlyDictionary<string, object?> _variables;
        private readonly List<Token> _tokens;
        private int _index;

        public Parser(string formula, IReadOnlyDictionary<string, object?> variables)
        {
            _variables = variables;
            _tokens = Tokenize(formula);
        }

        public object? ParseExpression() => ParseOr();

        public void EnsureCompleted()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw new FormulaException($"Unexpected token '{Current.Text}'.");
            }
        }

        private object? ParseOr()
        {
            var value = ParseAnd();
            while (Match(TokenKind.Or))
            {
                value = CustomSignalRuntimeHelper.ToBool(value) || CustomSignalRuntimeHelper.ToBool(ParseAnd());
            }

            return value;
        }

        private object? ParseAnd()
        {
            var value = ParseEquality();
            while (Match(TokenKind.And))
            {
                value = CustomSignalRuntimeHelper.ToBool(value) && CustomSignalRuntimeHelper.ToBool(ParseEquality());
            }

            return value;
        }

        private object? ParseEquality()
        {
            var value = ParseComparison();
            while (true)
            {
                if (Match(TokenKind.Equal))
                {
                    value = AreEqual(value, ParseComparison());
                    continue;
                }

                if (Match(TokenKind.NotEqual))
                {
                    value = !AreEqual(value, ParseComparison());
                    continue;
                }

                return value;
            }
        }

        private object? ParseComparison()
        {
            var value = ParseTerm();
            while (true)
            {
                if (Match(TokenKind.Greater))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) > CustomSignalRuntimeHelper.ToDouble(ParseTerm());
                    continue;
                }

                if (Match(TokenKind.GreaterOrEqual))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) >= CustomSignalRuntimeHelper.ToDouble(ParseTerm());
                    continue;
                }

                if (Match(TokenKind.Less))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) < CustomSignalRuntimeHelper.ToDouble(ParseTerm());
                    continue;
                }

                if (Match(TokenKind.LessOrEqual))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) <= CustomSignalRuntimeHelper.ToDouble(ParseTerm());
                    continue;
                }

                return value;
            }
        }

        private object? ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                if (Match(TokenKind.Plus))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) + CustomSignalRuntimeHelper.ToDouble(ParseFactor());
                    continue;
                }

                if (Match(TokenKind.Minus))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) - CustomSignalRuntimeHelper.ToDouble(ParseFactor());
                    continue;
                }

                return value;
            }
        }

        private object? ParseFactor()
        {
            var value = ParsePower();
            while (true)
            {
                if (Match(TokenKind.Multiply))
                {
                    value = CustomSignalRuntimeHelper.ToDouble(value) * CustomSignalRuntimeHelper.ToDouble(ParsePower());
                    continue;
                }

                if (Match(TokenKind.Divide))
                {
                    var divisor = CustomSignalRuntimeHelper.ToDouble(ParsePower());
                    value = Math.Abs(divisor) < double.Epsilon ? 0d : CustomSignalRuntimeHelper.ToDouble(value) / divisor;
                    continue;
                }

                return value;
            }
        }

        private object? ParsePower()
        {
            var value = ParseUnary();
            while (Match(TokenKind.Power))
            {
                value = Math.Pow(CustomSignalRuntimeHelper.ToDouble(value), CustomSignalRuntimeHelper.ToDouble(ParseUnary()));
            }

            return value;
        }

        private object? ParseUnary()
        {
            if (Match(TokenKind.Not))
            {
                return !CustomSignalRuntimeHelper.ToBool(ParseUnary());
            }

            if (Match(TokenKind.Minus))
            {
                return -CustomSignalRuntimeHelper.ToDouble(ParseUnary());
            }

            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            if (Match(TokenKind.Number, out var numberToken))
            {
                return double.Parse(numberToken.Text, CultureInfo.InvariantCulture);
            }

            if (Match(TokenKind.String, out var stringToken))
            {
                return stringToken.Text;
            }

            if (Match(TokenKind.Boolean, out var booleanToken))
            {
                return bool.Parse(booleanToken.Text);
            }

            if (Match(TokenKind.Variable, out var variableToken))
            {
                if (!_variables.TryGetValue(variableToken.Text, out var variableValue))
                {
                    throw new FormulaException($"Unknown variable '{variableToken.Text}'.");
                }

                return variableValue;
            }

            if (Match(TokenKind.Identifier, out var identifierToken))
            {
                if (!Match(TokenKind.LeftParen))
                {
                    throw new FormulaException($"Function '{identifierToken.Text}' requires parentheses.");
                }

                var arguments = new List<object?>();
                if (!Match(TokenKind.RightParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    }
                    while (Match(TokenKind.Comma));

                    Expect(TokenKind.RightParen);
                }

                return EvaluateFunction(identifierToken.Text, arguments);
            }

            if (Match(TokenKind.LeftParen))
            {
                var value = ParseExpression();
                Expect(TokenKind.RightParen);
                return value;
            }

            throw new FormulaException($"Unexpected token '{Current.Text}'.");
        }

        private object? EvaluateFunction(string name, IReadOnlyList<object?> arguments)
        {
            return name.ToLowerInvariant() switch
            {
                "sqrt" => Math.Sqrt(GetNumericArgument(name, arguments, 0, expectedCount: 1)),
                "abs" => Math.Abs(GetNumericArgument(name, arguments, 0, expectedCount: 1)),
                "min" => Math.Min(GetNumericArgument(name, arguments, 0, expectedCount: 2), GetNumericArgument(name, arguments, 1, expectedCount: 2)),
                "max" => Math.Max(GetNumericArgument(name, arguments, 0, expectedCount: 2), GetNumericArgument(name, arguments, 1, expectedCount: 2)),
                "pow" => Math.Pow(GetNumericArgument(name, arguments, 0, expectedCount: 2), GetNumericArgument(name, arguments, 1, expectedCount: 2)),
                "if" => GetBooleanArgument(name, arguments, 0, expectedCount: 3) ? arguments[1] : arguments[2],
                "concat" => string.Concat(arguments.Select(argument => argument?.ToString() ?? string.Empty)),
                _ => throw new FormulaException($"Unknown function '{name}'.")
            };
        }

        private double GetNumericArgument(string functionName, IReadOnlyList<object?> arguments, int index, int expectedCount)
        {
            ValidateArgumentCount(functionName, arguments, expectedCount);
            return CustomSignalRuntimeHelper.ToDouble(arguments[index]);
        }

        private bool GetBooleanArgument(string functionName, IReadOnlyList<object?> arguments, int index, int expectedCount)
        {
            ValidateArgumentCount(functionName, arguments, expectedCount);
            return CustomSignalRuntimeHelper.ToBool(arguments[index]);
        }

        private static void ValidateArgumentCount(string functionName, IReadOnlyList<object?> arguments, int expectedCount)
        {
            if (arguments.Count != expectedCount)
            {
                throw new FormulaException($"Function '{functionName}' expects {expectedCount} argument(s).");
            }
        }

        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind)
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool Match(TokenKind kind, out Token token)
        {
            token = Current;
            if (token.Kind != kind)
            {
                return false;
            }

            _index++;
            return true;
        }

        private void Expect(TokenKind kind)
        {
            if (!Match(kind))
            {
                throw new FormulaException($"Expected '{kind}'.");
            }
        }

        private Token Current => _tokens[Math.Min(_index, _tokens.Count - 1)];

        private static List<Token> Tokenize(string formula)
        {
            var tokens = new List<Token>();
            for (var index = 0; index < formula.Length; index++)
            {
                var current = formula[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                if (current == '{')
                {
                    var endIndex = formula.IndexOf('}', index + 1);
                    if (endIndex < 0)
                    {
                        throw new FormulaException("Missing closing '}' for variable token.");
                    }

                    var variableName = formula[(index + 1)..endIndex].Trim();
                    if (string.IsNullOrWhiteSpace(variableName))
                    {
                        throw new FormulaException("Variable token must not be empty.");
                    }

                    tokens.Add(new Token(TokenKind.Variable, variableName));
                    index = endIndex;
                    continue;
                }

                if (current == '\'' || current == '"')
                {
                    var quote = current;
                    var builder = new StringBuilder();
                    var closed = false;
                    while (index + 1 < formula.Length)
                    {
                        index++;
                        var stringCharacter = formula[index];
                        if (stringCharacter == '\\')
                        {
                            if (index + 1 >= formula.Length)
                            {
                                throw new FormulaException("String literal ends with an incomplete escape sequence.");
                            }

                            index++;
                            builder.Append(formula[index] switch
                            {
                                '\\' => '\\',
                                '\'' => '\'',
                                '"' => '"',
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                var escaped => escaped
                            });
                            continue;
                        }

                        if (stringCharacter == quote)
                        {
                            closed = true;
                            break;
                        }

                        builder.Append(stringCharacter);
                    }

                    if (!closed)
                    {
                        throw new FormulaException("Missing closing quote for string literal.");
                    }

                    tokens.Add(new Token(TokenKind.String, builder.ToString()));
                    continue;
                }

                if (char.IsDigit(current) || current == '.')
                {
                    var start = index;
                    while (index + 1 < formula.Length && (char.IsDigit(formula[index + 1]) || formula[index + 1] == '.'))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenKind.Number, formula[start..(index + 1)]));
                    continue;
                }

                if (char.IsLetter(current) || current == '_')
                {
                    var start = index;
                    while (index + 1 < formula.Length && (char.IsLetterOrDigit(formula[index + 1]) || formula[index + 1] == '_'))
                    {
                        index++;
                    }

                    var identifier = formula[start..(index + 1)];
                    if (string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenKind.Boolean, identifier.ToLowerInvariant()));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Identifier, identifier));
                    }

                    continue;
                }

                var next = index + 1 < formula.Length ? formula[index + 1] : '\0';
                switch (current)
                {
                    case '&' when next == '&':
                        tokens.Add(new Token(TokenKind.And, "&&"));
                        index++;
                        break;
                    case '|' when next == '|':
                        tokens.Add(new Token(TokenKind.Or, "||"));
                        index++;
                        break;
                    case '=' when next == '=':
                        tokens.Add(new Token(TokenKind.Equal, "=="));
                        index++;
                        break;
                    case '!' when next == '=':
                        tokens.Add(new Token(TokenKind.NotEqual, "!="));
                        index++;
                        break;
                    case '>' when next == '=':
                        tokens.Add(new Token(TokenKind.GreaterOrEqual, ">="));
                        index++;
                        break;
                    case '<' when next == '=':
                        tokens.Add(new Token(TokenKind.LessOrEqual, "<="));
                        index++;
                        break;
                    case '+':
                        tokens.Add(new Token(TokenKind.Plus, "+"));
                        break;
                    case '-':
                        tokens.Add(new Token(TokenKind.Minus, "-"));
                        break;
                    case '*':
                        tokens.Add(new Token(TokenKind.Multiply, "*"));
                        break;
                    case '/':
                        tokens.Add(new Token(TokenKind.Divide, "/"));
                        break;
                    case '^':
                        tokens.Add(new Token(TokenKind.Power, "^"));
                        break;
                    case '!':
                        tokens.Add(new Token(TokenKind.Not, "!"));
                        break;
                    case '>':
                        tokens.Add(new Token(TokenKind.Greater, ">"));
                        break;
                    case '<':
                        tokens.Add(new Token(TokenKind.Less, "<"));
                        break;
                    case '(':
                        tokens.Add(new Token(TokenKind.LeftParen, "("));
                        break;
                    case ')':
                        tokens.Add(new Token(TokenKind.RightParen, ")"));
                        break;
                    case ',':
                        tokens.Add(new Token(TokenKind.Comma, ","));
                        break;
                    default:
                        throw new FormulaException($"Unsupported character '{current}'.");
                }
            }

            tokens.Add(new Token(TokenKind.End, string.Empty));
            return tokens;
        }

        private static bool AreEqual(object? left, object? right)
        {
            if (left is bool || right is bool)
            {
                return CustomSignalRuntimeHelper.ToBool(left) == CustomSignalRuntimeHelper.ToBool(right);
            }

            if (CustomSignalRuntimeHelper.ToNullableDouble(left) is double leftNumber && CustomSignalRuntimeHelper.ToNullableDouble(right) is double rightNumber)
            {
                return Math.Abs(leftNumber - rightNumber) < 0.000001d;
            }

            return string.Equals(left?.ToString() ?? string.Empty, right?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private enum TokenKind
    {
        End,
        Number,
        String,
        Boolean,
        Identifier,
        Variable,
        LeftParen,
        RightParen,
        Comma,
        Plus,
        Minus,
        Multiply,
        Divide,
        Power,
        Not,
        And,
        Or,
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }

    private sealed class FormulaException : Exception
    {
        public FormulaException(string message)
            : base(message)
        {
        }
    }
}
