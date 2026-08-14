using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofValidationNotificationSourceContractTests
{
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string NotificationService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationService.cs");
    private static readonly string NotificationWindow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml.cs");
    private static readonly string NotificationXaml = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml");

    [Fact]
    public void OpenLoopRetainsExactSlovakTextAndDedicatedDescriptor()
    {
        Assert.Equal("Obrys strechy nie je uzavretý", Resource("Command_Roof_OpenLoopNotificationTitle"));
        Assert.Equal(
            "Spojte posledný bod s prvým alebo použite PLINE → Close.",
            Resource("Command_Roof_OpenLoopNotificationBody"));
        Assert.Contains("error == RoofValidationError.OpenLoop", ValidationMapping());
        Assert.Contains("notification = OpenLoopNotification", ValidationMapping());
    }

    [Fact]
    public void UnrelatedObjectUsesLocalizedNotificationWithoutCliDuplicate()
    {
        var selectionMapping = Member(
            "private static bool TryGetSelectionNotification",
            "private static bool TryGetValidationNotification");
        var selectionFailure = Member(
            "if (!resolution.IsResolved)",
            "ownerId = resolution.OwnerId");

        Assert.Contains("error == RoofOwnerSelectionError.UnrelatedObject", selectionMapping);
        Assert.Contains("notification = InvalidObjectNotification", selectionMapping);
        Assert.Contains("ShowNotification(notification)", selectionFailure);
        Assert.Contains("else", selectionFailure);
        Assert.Contains("editor.WriteMessage(GetSelectionMessage(resolution.Error))", selectionFailure);
        Assert.DoesNotContain("Command_Roof_SelectionInvalid", selectionFailure);
        Assert.Equal("Nevhodný objekt", Resource("Command_Roof_InvalidObjectNotificationTitle"));
        Assert.Equal(
            "Vyberte obrys alebo existujúcu časť strechy.",
            Resource("Command_Roof_InvalidObjectNotificationBody"));
    }

    [Fact]
    public void UnsupportedSimpleGableFootprintsUseTruthfulRectangularGuidance()
    {
        var mapping = GeometryMapping();
        Assert.Contains("SimpleGableRoofGeometryError.FootprintIsNotFourSided", mapping);
        Assert.Contains("SimpleGableRoofGeometryError.FootprintIsNotRectangular", mapping);
        Assert.Contains("notification = UnsupportedFootprintNotification", mapping);
        Assert.Equal(
            "Tento obrys nie je podporovaný",
            Resource("Command_Roof_UnsupportedFootprintNotificationTitle"));
        Assert.Equal(
            "Pre sedlovú strechu vyberte obdĺžnikový pôdorys.",
            Resource("Command_Roof_UnsupportedFootprintNotificationBody"));
    }

    [Fact]
    public void ExistingInvalidAndDegenerateFootprintsShareCorrectiveNotification()
    {
        var validation = ValidationMapping();
        foreach (var error in new[]
        {
            "UnsupportedCurvedSegment", "NonPlanar", "FewerThanThreeUniqueVertices",
            "NonFiniteCoordinate", "DuplicateConsecutiveVertex", "ZeroLengthEdge",
            "SelfIntersection", "DegenerateArea", "RedundantCollinearVertex",
        })
        {
            Assert.Contains($"RoofValidationError.{error}", validation);
        }

        Assert.Contains("notification = InvalidFootprintNotification", validation);
        Assert.Contains("SimpleGableRoofGeometryError.DegenerateDimensions", GeometryMapping());
        Assert.Equal("Obrys strechy nie je platný", Resource("Command_Roof_InvalidFootprintNotificationTitle"));
    }

    [Fact]
    public void ExistingSlopeAndDirectionFailuresUseSpecificNotifications()
    {
        var mapping = GeometryMapping();
        Assert.Contains("RidgeDirectionCannotBeResolved", mapping);
        Assert.Contains("notification = InvalidDirectionNotification", mapping);
        Assert.Contains("InvalidSlope", mapping);
        Assert.Contains("notification = InvalidSlopeNotification", mapping);

        var prompt = Member("private static bool TryPromptParameters", "private static void ShowNotification");
        Assert.Contains("editor.WriteMessage(UiStrings.GetString(\"Command_Roof_GeometryErrorDirection\"))", prompt);
        Assert.DoesNotContain("ShowNotification", prompt);
    }

    [Fact]
    public void CancelOrNothingSelectedExitsWithoutNotification()
    {
        var acquisition = Member(
            "var selected = editor.GetEntity(prompt)",
            "RoofValidationResult validation");
        Assert.Contains("selected.Status != PromptStatus.OK", acquisition);
        Assert.Contains("return;", acquisition);
        Assert.DoesNotContain("ShowNotification", acquisition);
    }

    [Fact]
    public void StaleSemanticAndDisplayWorkflowRemainCli()
    {
        var stored = Member("if (storedDefinition.Exists)", "if (!TryPromptParameters");
        Assert.Contains("Command_Roof_PersistedStale", stored);
        Assert.Contains("Command_Roof_DisplayStale", stored);
        Assert.Contains("ConfirmDisplayPersistence", stored);
        Assert.Contains("editor.WriteMessage", stored);
        Assert.DoesNotContain("ShowNotification", stored);
    }

    [Fact]
    public void TechnicalSelectionAndNonFiniteSolverFailuresRemainCli()
    {
        var selectionMapping = Member(
            "private static bool TryGetSelectionNotification",
            "private static bool TryGetValidationNotification");
        Assert.DoesNotContain("MalformedDisplayMetadata", selectionMapping);
        Assert.DoesNotContain("UnsupportedFutureDisplaySchema", selectionMapping);
        Assert.DoesNotContain("InvalidOwnerReference", selectionMapping);
        Assert.DoesNotContain("MissingOwner", selectionMapping);
        Assert.DoesNotContain("OwnerIsNotPolyline", selectionMapping);
        Assert.DoesNotContain("NonFiniteGeometry", GeometryMapping());
        Assert.Contains("GetGeometryMessage(geometryResult.Error)", Workflow);
    }

    [Fact]
    public void NotificationRoutingIsReadOnlyAndRetriesInsideOneCommandLoop()
    {
        var routing = Member("while (true)", "private static void ShowPreview");
        var notificationMembers = Member(
            "private static void ShowNotification",
            "private static string GetValidationMessage");
        var source = routing + notificationMembers + NotificationService + NotificationWindow;

        Assert.Contains("while (true)", routing);
        Assert.Contains("continue;", routing);
        Assert.Equal(1, Count(Workflow, "TransientNotificationService.Show"));
        Assert.DoesNotContain("RoofCommandWorkflow.Run", notificationMembers);
        Assert.DoesNotContain("DocumentLock", notificationMembers + NotificationService + NotificationWindow);
        Assert.DoesNotContain("OpenMode.ForWrite", notificationMembers);
        Assert.DoesNotContain("transaction.Commit", notificationMembers);
        Assert.DoesNotContain("EnsureRegAppRegistered", source);
        Assert.DoesNotContain("ApplyDisplayLayer", source);
        Assert.DoesNotContain("EnsureGroup", source);
        Assert.DoesNotContain("RoofDisplayStore.Write", source);
    }

    [Fact]
    public void ProvenWindowLifecycleIsSharedAndUnchanged()
    {
        Assert.Contains("TimeSpan.FromMilliseconds(2500)", NotificationWindow);
        Assert.Contains("Loaded += TransientNotificationWindow_Loaded", NotificationWindow);
        Assert.Contains("DispatcherPriority.ApplicationIdle", NotificationWindow);
        Assert.Contains("ArmEscapeDismiss", NotificationWindow);
        Assert.DoesNotContain("MouseLeftButtonUp", NotificationWindow + NotificationXaml);
        Assert.DoesNotContain("MouseDown", NotificationWindow + NotificationXaml);
        Assert.Contains("AcApp.ShowModalWindow(window)", NotificationService);
    }

    private static string ValidationMapping() => Member(
        "private static bool TryGetValidationNotification",
        "private static bool TryGetGeometryNotification");

    private static string GeometryMapping() => Member(
        "private static bool TryGetGeometryNotification",
        "private static string GetValidationMessage");

    private static string Member(string start, string end) =>
        RoofUxSourceContractText.Member(Workflow, start, end);

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string Resource(string key)
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "src", "AcKrovy.Localization", "Resources", "UiStrings.resx");
        return XDocument.Load(path)
            .Root!.Elements("data")
            .Single(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            .Element("value")!.Value;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
