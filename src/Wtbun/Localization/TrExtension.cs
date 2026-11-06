using System.Windows.Data;
using System.Windows.Markup;

namespace Wtbun.Localization;

/// <summary>
/// XAML で画面の文言を使う：<c>Text="{l:Tr Config_Save}"</c>。
/// 言語を変えると表示も変わるよう、<see cref="Loc.Instance"/> のインデクサーへの Binding にする。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
