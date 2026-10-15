namespace PaintDotNet.Effects;

// Name-shape stand-ins for PDN's effect dialog roots (legacy
// EffectConfigDialog, GPU EffectConfigForm): the deferral policy
// matches by full type name (the browser host cannot reference the
// dynamically loaded PDN assemblies).
internal class EffectConfigDialog : System.Windows.Forms.Form
{
}

internal class EffectConfigForm : System.Windows.Forms.Form
{
}

internal class LevelsGpuEffectFormStandIn : EffectConfigForm
{
}
