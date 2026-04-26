using System;
using System.Diagnostics.CodeAnalysis;
using AgentDesktop.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AgentDesktop.Desktop;

/// <summary>
/// Resolves a view-model type to its corresponding view by naming
/// convention. Used by Avalonia DataTemplates to render any
/// <see cref="ViewModelBase"/> bound to a content host.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is not null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
