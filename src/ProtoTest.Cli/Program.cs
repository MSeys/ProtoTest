using System.Text;
using ProtoTest.Cli;

// The summary and diagnosis text carries the middle dot separator; a default Windows console needs
// UTF-8 to render it instead of a replacement glyph. Redirected output keeps parsing-friendly bytes:
// UTF-8 without a BOM, and a machine without a console handle is left alone.
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
}

return CliHost.Run(args, Console.Out, Console.Error);
