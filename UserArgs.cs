namespace RFPchecker;

public enum AnalysisMode
{
    [Description("Sjekk RFP for alle rom i viewet")]
    Analyze,
    [Description("Sjekk RFP kun for valgte rom")]
    AnalyzeSelected,
    [Description("Tilbakestill farger og fjern notater")]
    Reset
}

public class AssistantArgs
{
    [Description("Mode"), ControlData(ToolTip = "Velg modus")]
    public AnalysisMode Mode { get; set; } = AnalysisMode.Analyze;
}