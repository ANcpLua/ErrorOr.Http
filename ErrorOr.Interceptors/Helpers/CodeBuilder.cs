using System.Runtime.CompilerServices;
using System.Text;

namespace ANcpLua.Interceptors.ErrorOr.Generator.Helpers;

internal sealed class CodeBuilder
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _atLineStart = true;

    // Fix RS1035: Force deterministic newlines
    private const char NewLine = '\n';

    public int Length => _sb.Length;

    public CodeBuilder Indent()
    {
        _indent++;
        return this;
    }

    public CodeBuilder Outdent()
    {
        if (_indent > 0) _indent--;
        return this;
    }

    public CodeBuilder AppendLine()
    {
        _sb.Append(NewLine);
        _atLineStart = true;
        return this;
    }

    public CodeBuilder AppendLine(string value)
    {
        AppendIndentIfNeeded();
        _sb.Append(value);
        _sb.Append(NewLine);
        _atLineStart = true;
        return this;
    }

    public CodeBuilder AppendLine([InterpolatedStringHandlerArgument("")] CodeBuilderHandler handler)
    {
        _sb.Append(NewLine);
        _atLineStart = true;
        return this;
    }

    public CodeBuilder AppendLine(
        bool condition,
        [InterpolatedStringHandlerArgument("", nameof(condition))]
        ConditionalCodeBuilderHandler handler)
    {
        if (condition)
        {
            _sb.Append(NewLine);
            _atLineStart = true;
        }

        return this;
    }

    public CodeBuilder Append(string value)
    {
        AppendIndentIfNeeded();
        _sb.Append(value);
        return this;
    }

    public CodeBuilder Append([InterpolatedStringHandlerArgument("")] CodeBuilderHandler handler) => this;

    public CodeBuilder Append(
        bool condition,
        [InterpolatedStringHandlerArgument("", nameof(condition))]
        ConditionalCodeBuilderHandler handler) => this;

    private void AppendIndentIfNeeded()
    {
        if (_atLineStart && _indent > 0)
        {
            _sb.Append(' ', _indent * 4);
            _atLineStart = false;
        }
    }

    private void AppendRaw(string? value)
    {
        if (value is null) return;
        AppendIndentIfNeeded();
        _sb.Append(value);
    }

    private void AppendRaw(char value)
    {
        AppendIndentIfNeeded();
        _sb.Append(value);
    }

    private void AppendRaw(int value)
    {
        AppendIndentIfNeeded();
        _sb.Append(value);
    }

    private void AppendRaw(bool value)
    {
        AppendIndentIfNeeded();
        _sb.Append(value ? "true" : "false");
    }

    public override string ToString() => _sb.ToString();

    [InterpolatedStringHandler]
    public readonly ref struct CodeBuilderHandler
    {
        private readonly CodeBuilder _builder;
        public CodeBuilderHandler(int literalLength, int formattedCount, CodeBuilder builder) => _builder = builder;
        public void AppendLiteral(string value) => _builder.AppendRaw(value);
        public void AppendFormatted<T>(T value) => _builder.AppendRaw(value?.ToString());

        public void AppendFormatted<T>(System.Collections.Generic.IEnumerable<T> values, string? format)
        {
            var separator = format switch { "comma" => ", ", "space" => " ", "newline" => "\n", _ => format ?? ", " };
            var first = true;
            foreach (var item in values)
            {
                if (!first) _builder.AppendRaw(separator);
                _builder.AppendRaw(item?.ToString());
                first = false;
            }
        }
    }

    [InterpolatedStringHandler]
    public readonly ref struct ConditionalCodeBuilderHandler
    {
        private readonly CodeBuilder? _builder;
        private readonly bool _enabled;

        public ConditionalCodeBuilderHandler(int literalLength, int formattedCount, CodeBuilder builder, bool condition)
        {
            _enabled = condition;
            _builder = condition ? builder : null;
        }

        public void AppendLiteral(string value)
        {
            if (_enabled) _builder!.AppendRaw(value);
        }

        public void AppendFormatted<T>(T value)
        {
            if (_enabled) _builder!.AppendRaw(value?.ToString());
        }

        public void AppendFormatted<T>(System.Collections.Generic.IEnumerable<T> values, string? format)
        {
            if (!_enabled) return;
            var separator = format switch { "comma" => ", ", "space" => " ", "newline" => "\n", _ => format ?? ", " };
            var first = true;
            foreach (var item in values)
            {
                if (!first) _builder!.AppendRaw(separator);
                _builder!.AppendRaw(item?.ToString());
                first = false;
            }
        }
    }
}