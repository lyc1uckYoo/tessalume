using System.Windows;

namespace Tessalume.App.Features.Pets;

internal interface IPetCommandClipboard
{
    void Copy(string text);
}

internal sealed class SystemPetCommandClipboard : IPetCommandClipboard
{
    public void Copy(string text) => Clipboard.SetText(text, TextDataFormat.UnicodeText);
}
