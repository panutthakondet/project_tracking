using System.Text;
namespace ProjectTracking.Services;

// Palette adaptation, not a simulation/filter of a person's vision. Labels remain authoritative.
public static class ColorVisionTheme
{
    public static string BuildCss(bool enabled)
    {
        if(!enabled)return ":root{--pt-colorblind-enabled:0;}";
        var palette=new Dictionary<string,string>{{"primary","#CC79A7"},{"success","#0072B2"},{"warning","#E6B800"},{"danger","#B44A00"},{"info","#56B4E9"},{"alt","#8C6BB1"},{"muted","#707070"}};
        var css=new StringBuilder(":root{--pt-colorblind-enabled:1;");
        foreach(var (tone,color) in palette)
            css.Append($"--pt-chart-{tone}:{color};--pt-chart-{tone}-light:color-mix(in srgb,{color} 65%,white);--pt-chart-{tone}-dark:color-mix(in srgb,{color} 70%,black);--pt-chart-{tone}-soft:color-mix(in srgb,{color} 18%,transparent);");
        css.Append("}");
        foreach(var (state,tone,mark) in new[]{("success","success","✓"),("warning","warning","!"),("danger","danger","×"),("info","info","i")})
        {
            css.Append($"html body :is(.bg-{state},.text-bg-{state},.badge-{state}){{background:var(--pt-chart-{tone})!important;color:#fff!important;border:2px solid currentColor;}}");
            if(state is "warning" or "info")css.Append($"html body :is(.bg-{state},.text-bg-{state},.badge-{state}){{color:#17212b!important;}}");
            css.Append($"html body :is(.badge.bg-{state},.badge.text-bg-{state},.badge-{state})::before{{content:'{mark} ';font-weight:800;}}");
            css.Append($"html body .alert-{state}{{color:var(--pt-surface-contrast)!important;background:var(--pt-surface)!important;border:2px solid var(--pt-chart-{tone})!important;border-left-width:7px!important;}}");
        }
        var owner=new[]{("completed-project","muted"),("done","success"),("working","warning"),("stuck","danger"),("issue-open","alt"),("support-open","primary"),("field-service","info")};
        foreach(var (name,tone) in owner)
            css.Append($"html body #ownerOverview .{name}{{--owner-base:var(--pt-chart-{tone})!important;--owner-light:var(--pt-chart-{tone}-light)!important;--owner-shadow:var(--pt-chart-{tone}-dark)!important;}}");
        css.Append("html body .dashboard-view .dot.lime{color:var(--pt-chart-success)!important;background:currentColor!important;}html body .dashboard-view .dot.warning,html body .dashboard-view .dot.orange{border-radius:0!important;transform:rotate(45deg);}html body .dashboard-view .dot.danger,html body .dashboard-view .dot.pink{border-radius:0!important;border:1px dashed currentColor;}");
        css.Append("html body #ownerOverview .owner-overview-legend .completed-project{border:2px solid currentColor;border-radius:0!important;}html body #ownerOverview .owner-overview-legend .working{border-radius:0!important;transform:rotate(45deg);}html body #ownerOverview .owner-overview-legend .stuck{border:2px dashed currentColor;border-radius:0!important;}");
        return css.ToString();
    }
}
