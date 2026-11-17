using System.Text;
namespace ProjectTracking.Services;

// Palette adaptation, not a simulation/filter of a person's vision. Labels remain authoritative.
public static class ColorVisionTheme
{
    public static string NormalizeMode(string? mode) => mode is "blue-yellow" or "monochrome" ? mode : "red-green";
    public static IReadOnlyDictionary<string,string> GetPalette(string mode)
    {
        var palette=new Dictionary<string,string>{{"primary","#CC79A7"},{"success","#0072B2"},{"warning","#E6B800"},{"danger","#B44A00"},{"info","#56B4E9"},{"alt","#8C6BB1"},{"muted","#707070"}};
        mode = NormalizeMode(mode);
        if(mode == "blue-yellow") palette = new() {{"primary","#8D2459"},{"success","#006B58"},{"warning","#E88B97"},{"danger","#9E2900"},{"info","#A5D8C9"},{"alt","#5B426B"},{"muted","#747474"}};
        if(mode == "monochrome") palette = new() {{"primary","#C5C5C5"},{"success","#E6E6E6"},{"warning","#777777"},{"danger","#B3B3B3"},{"info","#FFFFFF"},{"alt","#999999"},{"muted","#5C5C5C"}};
        return palette;
    }
    public static double Luminance(string hex)
    {
        double Channel(int offset) { var c=Convert.ToInt32(hex.Substring(offset,2),16)/255d; return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4); }
        return .2126*Channel(1)+.7152*Channel(3)+.0722*Channel(5);
    }
    public static double Contrast(string a,string b) { var x=Luminance(a);var y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05); }
    public static string TextOn(string background) => Contrast(background,"#000000") >= Contrast(background,"#FFFFFF") ? "#000000" : "#FFFFFF";
    public static string BuildCss(bool enabled, string mode = "red-green")
    {
        if(!enabled)return ":root{--pt-colorblind-enabled:0;}";
        var palette=GetPalette(mode);
        var css=new StringBuilder($":root{{--pt-colorblind-enabled:1;--pt-colorblind-mode:{NormalizeMode(mode)};");
        foreach(var (tone,color) in palette)
            css.Append($"--pt-chart-{tone}:{color};--pt-chart-{tone}-ink:{TextOn(color)};--pt-chart-{tone}-light:color-mix(in srgb,{color} 65%,white);--pt-chart-{tone}-dark:color-mix(in srgb,{color} 70%,black);--pt-chart-{tone}-soft:color-mix(in srgb,{color} 18%,transparent);");
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
            var ink = TextOn(palette[tone]);
            css.Append($"html body .btn-{state}{{background:var(--pt-chart-{tone})!important;color:{ink}!important;-webkit-text-fill-color:{ink}!important;border-color:currentColor!important;}}html body .btn-outline-{state}{{color:var(--pt-accent)!important;background:var(--pt-surface)!important;border-color:currentColor!important;}}");
            css.Append($"html body :is(.bg-{state},.text-bg-{state},.badge-{state}){{background:var(--pt-chart-{tone})!important;color:{ink}!important;-webkit-text-fill-color:{ink}!important;border:2px solid currentColor;}}");

            css.Append($"html body :is(.badge.bg-{state},.badge.text-bg-{state},.badge-{state})::before{{content:'{mark} ';font-weight:800;}}");
            css.Append($"html body .alert-{state}{{color:var(--pt-surface-contrast)!important;background:var(--pt-surface)!important;border:2px solid var(--pt-chart-{tone})!important;border-left-width:7px!important;}}");
        }
        foreach(var (status,mark) in new[]{("OPEN","O"),("WIP","W"),("FIXED","F"),("FAIL","!"),("PASS","✓"),("REJECT","×")})
            css.Append($"html body .dashboard-view :is([data-overview-metric],[data-support-metric])[data-status='{status}'] .metric-name::before{{content:'{mark} ';font-weight:800;}}");
        css.Append("html body .dashboard-view [data-project-status-tone='blue'] .metric-name::before{content:'P ';}html body .dashboard-view :is([data-project-status-tone='green'],[data-project-status-tone='lime']) .metric-name::before{content:'✓ ';}html body .dashboard-view [data-assign-neon-metric][data-state='DONE'] .metric-name::before{content:'✓ ';}html body .dashboard-view [data-assign-neon-metric][data-state='WARNING'] .metric-name::before{content:'! ';}html body .dashboard-view [data-assign-neon-metric][data-state='DANGER'] .metric-name::before{content:'× ';}");
        css.Append("html body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) > :is(h1,h2,h3):first-child,html body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) > :is(.container,.container-fluid):first-child > :is(h1,h2,h3):first-child{--pt-local-text:var(--pt-sidebar-contrast)!important;--pt-local-muted:var(--pt-sidebar-contrast-muted)!important;color:var(--pt-sidebar-contrast)!important;-webkit-text-fill-color:var(--pt-sidebar-contrast)!important;}");
        foreach(var (state,tone) in new[]{("primary","primary"),("success","success"),("warning","warning"),("danger","danger"),("info","info")}){
            var foreground=state=="primary"?"var(--pt-accent-contrast)":TextOn(palette[tone]);
            css.Append($"html body main :is(.index-actions,.hero-actions) .btn.btn-{state}:not(.btn-close){{--pt-button-readable:{foreground}!important;--pt-action-color:{foreground}!important;color:{foreground}!important;-webkit-text-fill-color:{foreground}!important;}}");
        }
        var owner=new[]{("completed-project","muted"),("done","success"),("working","warning"),("stuck","danger"),("issue-open","alt"),("support-open","primary"),("field-service","info")};
        foreach(var (name,tone) in owner)
            css.Append($"html body #ownerOverview .{name}{{--owner-base:var(--pt-chart-{tone})!important;--owner-light:var(--pt-chart-{tone}-light)!important;--owner-shadow:var(--pt-chart-{tone}-dark)!important;}}");
        css.Append("html body .dashboard-view .dot.lime{color:var(--pt-chart-success)!important;background:currentColor!important;}html body .dashboard-view .dot.warning,html body .dashboard-view .dot.orange{border-radius:0!important;transform:rotate(45deg);}html body .dashboard-view .dot.danger,html body .dashboard-view .dot.pink{border-radius:0!important;border:1px dashed currentColor;}");
        css.Append("html body #ownerOverview .owner-overview-legend .completed-project{border:2px solid currentColor;border-radius:0!important;}html body #ownerOverview .owner-overview-legend .working{border-radius:0!important;transform:rotate(45deg);}html body #ownerOverview .owner-overview-legend .stuck{border:2px dashed currentColor;border-radius:0!important;}");
        css.Append("html body :focus-visible{outline:3px solid #fff!important;outline-offset:2px;box-shadow:0 0 0 5px #111!important;}html body :is(.form-control,.form-select).is-invalid{border:2px dashed var(--pt-accent)!important;}html body .invalid-feedback{color:var(--pt-text)!important;font-weight:700;}html body .invalid-feedback::before{content:'! ';}");
        css.Append("html body .dashboard-view .dot{box-shadow:0 0 0 1px #fff,0 0 0 2px #111!important;}html body .dashboard-view .dot:is(.orange,.warning){border-radius:0!important;}html body .dashboard-view .dot:is(.pink,.danger){clip-path:polygon(50% 0,100% 100%,0 100%);}");
        var patterns=new[]{"repeating-linear-gradient(0deg,transparent 0 5px,#0009 5px 7px)","repeating-linear-gradient(45deg,transparent 0 5px,#0009 5px 7px)","repeating-linear-gradient(90deg,transparent 0 5px,#0009 5px 7px)","repeating-linear-gradient(-45deg,transparent 0 5px,#0009 5px 7px)","repeating-linear-gradient(0deg,transparent 0 3px,#0009 3px 4px)","repeating-linear-gradient(90deg,transparent 0 3px,#0009 3px 4px)","repeating-linear-gradient(45deg,transparent 0 2px,#0009 2px 4px)"};
        for(var i=0;i<owner.Length;i++){
            var name=owner[i].Item1;
            css.Append($"html body main .dashboard-view #ownerOverview .owner-bar.{name}:not(.is-zero) i{{background-image:{patterns[i]},linear-gradient(var(--owner-light),var(--owner-base),var(--owner-shadow))!important;border:1px solid #fff;box-sizing:border-box;}}html body main .dashboard-view #ownerOverview :is(.owner-overview-legend,.owner-overview-footer) i.{name}{{background-image:{patterns[i]}!important;border:1px solid currentColor;transform:none!important;}}");
        }
        if(NormalizeMode(mode)=="monochrome")css.Append("html body .dashboard-view :is(.dot,.dashboard-global-tool-icon,.section-icon,.kpi-icon){filter:grayscale(1)!important;}");
        return css.ToString();
    }
}
