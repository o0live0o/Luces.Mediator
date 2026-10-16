using System;
using System.Text;

namespace Luces.Mediator.SourceGen;

internal sealed class CodeWriter
{
    private const int _indentSize = 4;
    private readonly StringBuilder _sb = new StringBuilder();
    private int _indentLevel;

    public void WriteLine(string line = "")
    {
        if (line.Length > 0)
        {
            _sb.Append(' ', checked(_indentLevel * _indentSize));
            _sb.Append(line);
        }
        _sb.Append("\n");
    }

    public IDisposable Indent()
    {
        _indentLevel++;
        return new Scope(this, closingLine: null);
    }

    public IDisposable Block(string header)
    {
        WriteLine(header);
        WriteLine("{");
        _indentLevel++;
        return new Scope(this, "}");
    }

    public override string ToString()
    {
        return _sb.ToString();
    }

    public string ToStringWithoutFinalNewLine()
    {
        return _sb.Length > 0 && _sb[_sb.Length - 1] == '\n' ? _sb.ToString(0, _sb.Length - 1) : _sb.ToString();
    }

    private void EndScope(string? closingLine)
    {
        if (_indentLevel == 0)
        {
            throw new InvalidOperationException("Cannot end thr root indentation scope");
        }

        _indentLevel--;

        if (closingLine is not null)
        {
            WriteLine(closingLine);
        }
    }

    private sealed class Scope(CodeWriter writer, string? closingLine) : IDisposable
    {
        private CodeWriter? _writer = writer;

        public void Dispose()
        {
            var current = _writer;
            if (current is null)
                return;

            _writer = null;
            current.EndScope(closingLine);
        }
    }
}
