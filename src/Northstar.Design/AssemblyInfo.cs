using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// One XAML namespace for the whole design system: xmlns:ns="https://northstarlabs.dev/design"
[assembly: XmlnsDefinition("https://northstarlabs.dev/design", "Northstar.Design.Controls")]
[assembly: XmlnsDefinition("https://northstarlabs.dev/design", "Northstar.Design.Icons")]
[assembly: XmlnsDefinition("https://northstarlabs.dev/design", "Northstar.Design.Branding")]
[assembly: XmlnsDefinition("https://northstarlabs.dev/design", "Northstar.Design.Dialogs")]
[assembly: XmlnsPrefix("https://northstarlabs.dev/design", "ns")]
