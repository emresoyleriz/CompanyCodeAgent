using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CompanyCodeAgent.VisualStudio;

[Guid("cfb4cda1-7df8-4c37-81c0-9280cbfd2ea8")]
public sealed class AgentToolWindow : ToolWindowPane
{
    public AgentToolWindow() : base(null)
    {
        Caption = "Company Code Agent";
        Content = new AgentToolWindowControl();
    }

    public void SetPrompt(string prompt, string mode = null) => ((AgentToolWindowControl)Content).SetPrompt(prompt, mode);
}
