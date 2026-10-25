namespace HexaWall;

internal sealed class DarkColorTable : ProfessionalColorTable
{
    private static Color Background => ColorTranslator.FromHtml("#181A20");
    private static Color Hover => ColorTranslator.FromHtml("#252A34");
    public override Color ToolStripDropDownBackground => Background;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Hover;
    public override Color MenuItemPressedGradientEnd => Hover;
    public override Color MenuBorder => IconFactory.Accent;
    public override Color MenuItemBorder => IconFactory.Accent;
    public override Color SeparatorDark => Hover;
    public override Color SeparatorLight => Background;
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;
}

internal sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new DarkColorTable())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.White : Color.Gray;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = IconFactory.Accent; base.OnRenderArrow(e);
    }
}
