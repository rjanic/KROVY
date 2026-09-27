using Autodesk.AutoCAD.DatabaseServices;
using System.IO;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Run only in a new disposable CoreConsole drawing. No KROVY production assembly.
public sealed class Probe
{
    private static void Log(string text)
    {
        File.AppendAllText("group-probe.txt", text + Environment.NewLine);
        App.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nGROUP_PROBE " + text);
    }

    [CommandMethod("AK_GROUP_UNERASE_PROBE")]
    public void Run()
    {
        try
        {
            var db = App.DocumentManager.MdiActiveDocument.Database;
            foreach (var scenario in new[] { "unerase-only", "open-group-write", "canonical-sync" })
                RunScenario(db, scenario);
            Log("COMPLETED");
        }
        catch (System.Exception ex) { Log("ERROR " + ex); }
    }

    private static void RunScenario(Database db, string scenario)
    {
        ObjectId ownerId, groupId;
        var expected = new List<ObjectId>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            Log(scenario + " setup-polyline");
            var source = new Polyline();
            source.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            source.AddVertexAt(1, new Point2d(10000, 0), 0, 0, 0);
            source.AddVertexAt(2, new Point2d(10000, 6000), 0, 0, 0);
            source.AddVertexAt(3, new Point2d(0, 6000), 0, 0, 0);
            source.Closed = true;
            ownerId = ms.AppendEntity(source);
            tr.AddNewlyCreatedDBObject(source, true);
            expected.Add(ownerId);
            for (var i = 0; i < 5; i++)
            {
                var line = new Line(new Point3d(i, 1, 0), new Point3d(i, 2, 0));
                expected.Add(ms.AppendEntity(line));
                tr.AddNewlyCreatedDBObject(line, true);
            }
            var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            var group = new Group("probe", true);
            groupId = dictionary.SetAt("GROUP_PROBE_" + scenario, group);
            tr.AddNewlyCreatedDBObject(group, true);
            foreach (var id in expected) group.Append(id);
            tr.Commit();
        }
        Dump(db, ownerId, groupId, scenario + ":initial");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ((Polyline)tr.GetObject(ownerId, OpenMode.ForWrite)).Erase();
            tr.Commit();
        }
        Dump(db, ownerId, groupId, scenario + ":erased");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var owner = (Polyline)tr.GetObject(ownerId, OpenMode.ForWrite, true);
            Dump(tr, ownerId, groupId, scenario + ":before-unerase");
            owner.Erase(false);
            Dump(tr, ownerId, groupId, scenario + ":after-unerase");
            if (scenario != "unerase-only")
            {
                var group = (Group)tr.GetObject(groupId, OpenMode.ForWrite);
                if (scenario == "canonical-sync") Normalize(group, expected, scenario);
            }
            Dump(tr, ownerId, groupId, scenario + ":before-commit");
            tr.Commit();
            Dump(db, ownerId, groupId, scenario + ":committed-inside-scope");
        }
        Dump(db, ownerId, groupId, scenario + ":closed");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            Normalize((Group)tr.GetObject(groupId, OpenMode.ForWrite), expected, scenario + ":finalize");
            tr.Commit();
        }
        Dump(db, ownerId, groupId, scenario + ":finalized-closed");
    }

    private static void Normalize(Group group, IReadOnlyCollection<ObjectId> expected, string phase)
    {
        foreach (var id in group.GetAllEntityIds().Distinct())
            while (group.GetAllEntityIds().Count(member => member == id) > (expected.Contains(id) ? 1 : 0))
            {
                var before = group.GetAllEntityIds().Length;
                group.Remove(id);
                Log(phase + " REMOVE member=" + id.Handle + " before=" + before + " after=" + group.GetAllEntityIds().Length);
            }
        foreach (var id in expected)
            if (!group.GetAllEntityIds().Contains(id))
            {
                group.Append(id);
                Log(phase + " APPEND member=" + id.Handle);
            }
    }

    private static void Dump(Database db, ObjectId ownerId, ObjectId groupId, string phase)
    {
        using var tr = db.TransactionManager.StartTransaction();
        Dump(tr, ownerId, groupId, phase);
    }
    private static void Dump(Transaction tr, ObjectId ownerId, ObjectId groupId, string phase)
    {
        var group = (Group)tr.GetObject(groupId, OpenMode.ForRead);
        var ids = group.GetAllEntityIds();
        var owner = (Polyline)tr.GetObject(ownerId, OpenMode.ForRead, true);
        Log(phase + " owner=" + ownerId.Handle + " erased=" + owner.IsErased + " group=" + groupId.Handle +
            " raw=" + ids.Length + " unique=" + ids.Distinct().Count() + " sourceSlots=" + ids.Count(id => id == ownerId) +
            " members=" + string.Join("|", ids.Select(id => id.Handle)));
    }
}
