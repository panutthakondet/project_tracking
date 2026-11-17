using System.Text;
namespace ProjectTracking.Services;

// Palette adaptation, not a simulation/filter of a person's vision. Labels remain authoritative.
public static class ColorVisionTheme
{
    public static string NormalizeMode(string? mode) => mode is "blue-yellow" or "monochrome" ? mode : "red-green";
    public static string BuildCss(bool enabled, string mode = "red-green")
    {
        if(!enabled)return ":root{--pt-colorblind-enabled:0;}";
        var palette=new Dictionary<string,string>{{"primary","#CC79A7"},{"success","#0072B2"},{"warning","#E6B800"},{"danger","#B44A00"},{"info","#56B4E9"},{"alt","#8C6BB1"},{"muted","#707070"}};
        mode = NormalizeMode(mode);
        if(mode == "blue-yellow") palette = new() {{"primary","#8D2459"},{"success","#006B58"},{"warning","#E88B97"},{"danger","#9E2900"},{"info","#A5D8C9"},{"alt","#5B426B"},{"muted","#747474"}};
        if(mode == "monochrome") palette = new() {{"primary","#444444"},{"success","#333333"},{"warning","#BBBBBB"},{"danger","#666666"},{"info","#DDDDDD"},{"alt","#888888"},{"muted","#AAAAAA"}};
        var css=new StringBuilder(":root{--pt-colorblind-enabled:1;");
        foreach(var (tone,color) in palette)
            css.Append($"--pt-chart-{tone}:{color};--pt-chart-{tone}-light:color-mix(in srgb,{color} 65%,white);--pt-chart-{tone}-dark:color-mix(in srgb,{color} 70%,black);--pt-chart-{tone}-soft:color-mix(in srgb,{color} 18%,transparent);");
        css.Append("}");
        // Shared controls across MVC pages, including standalone forms. Never filter photos or documents.
        css.Append("html body :is(.btn-primary,.btn-success,.btn-danger,.btn-warning,.btn-info){background:var(--pt-accent)!important;color:var(--pt-accent-contrast)!important;border:2px solid currentColor;}");
        css.Append("html body :is(.btn-outline-primary,.btn-outline-success,.btn-outline-danger,.btn-outline-warning,.btn-outline-info){color:var(--pt-accent)!important;background:var(--pt-surface)!important;border:2px solid currentColor!important;}");
        css.Append("html body :is(.text-success,.text-danger,.text-warning,.text-info,.text-primary){color:var(--pt-accent)!important;}html body a:not(.btn):focus-visible,html body :is(button,input,select,textarea):focus-visible{outline:3px solid var(--pt-accent)!important;outline-offset:3px;}");
        css.Append("html body :is(.table-success,.table-danger,.table-warning,.table-info){--bs-table-bg:var(--pt-surface-soft);--bs-table-color:var(--pt-text);border-left:5px solid var(--pt-accent);}");
        css.Append("html body :is(.form-check-input:checked,.page-item.active .page-link){background-color:var(--pt-accent)!important;border-color:var(--pt-accent)!important;color:var(--pt-accent-contrast)!important;}html body .progress-bar{background:var(--pt-accent)!important;}");
        css.Append("html body .dashboard-view .kpi-card{background:linear-gradient(135deg,var(--pt-dashboard-chart-panel-bg),var(--pt-dashboard-chart-panel-deep))!important;color:var(--pt-dashboard-chart-panel-contrast)!important;border-color:var(--pt-accent)!important;}");
        css.Append("html body :is(.dashboard-global-tool-icon,.section-icon,.kpi-icon){background:var(--pt-accent-soft)!important;color:var(--pt-accent)!important;border-color:var(--pt-accent)!important;box-shadow:none!important;}html body .dashboard-view :is(.kpi-wave,.kpi-bars,.kpi-dots){opacity:.12!important;}");
        if(mode == "monochrome") css.Append("html body :is(.nav-ico,.section-icon,.kpi-icon) img{filter:grayscale(1)!important;}");
        foreach(var (state,tone,mark) in new[]{("success","success","✓"),("warning","warning","!"),("danger","danger","×"),("info","info","i")})
        {
            var ink = state is "warning" or "info" ? "#17212b" : "#ffffff";
            css.Append($"html body .btn-{state}{{background:var(--pt-chart-{tone})!important;color:{ink}!important;border-color:currentColor!important;}}html body .btn-outline-{state}{{color:var(--pt-accent)!important;background:var(--pt-surface)!important;border-color:currentColor!important;}}");
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
