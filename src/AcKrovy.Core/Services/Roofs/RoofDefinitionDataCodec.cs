using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Deterministic invariant codec for backward-compatible roof definitions.</summary>
public static class RoofDefinitionDataCodec
{
    private const char Separator = '|';
    private const string SimpleGableToken = "SimpleGable";
    private const string AsymmetricGableToken = "AsymmetricGable";
    private const string Edge01Token = "Edge01";
    private const string Edge12Token = "Edge12";
    private const string ClockwiseToken = "CW";
    private const string CounterClockwiseToken = "CCW";
    private const double UnitDirectionTolerance = 0.000000001d;

    public static string Encode(RoofDefinitionData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!TryValidate(data, out var error))
        {
            throw new ArgumentException($"Invalid roof definition data: {error}.", nameof(data));
        }

        return data.SchemaVersion switch
        {
            RoofDefinitionDataSchema.LegacyAbsoluteVersion => EncodeV1(data),
            RoofDefinitionDataSchema.TopologyVersion => EncodeV2(data),
            RoofDefinitionDataSchema.HybridLifecycleVersion => EncodeV3(data),
            RoofDefinitionDataSchema.DualSlopeVersion => EncodeV4(data),
            RoofDefinitionDataSchema.CurrentVersion => EncodeV5(data),
            _ => throw new ArgumentException("Unsupported roof schema.", nameof(data)),
        };
    }

    public static bool TryDecode(
        string? payload,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length == 0 || !int.TryParse(
                fields[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var schemaVersion))
        {
            return false;
        }

        if (schemaVersion > RoofDefinitionDataSchema.CurrentVersion)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedFutureSchema;
            return false;
        }

        return schemaVersion switch
        {
            RoofDefinitionDataSchema.LegacyAbsoluteVersion =>
                TryDecodeV1(fields, out data, out error),
            RoofDefinitionDataSchema.TopologyVersion =>
                TryDecodeV2(fields, out data, out error),
            RoofDefinitionDataSchema.HybridLifecycleVersion =>
                TryDecodeV3(fields, out data, out error),
            RoofDefinitionDataSchema.DualSlopeVersion =>
                TryDecodeV4(fields, out data, out error),
            RoofDefinitionDataSchema.CurrentVersion =>
                TryDecodeV5(fields, out data, out error),
            _ => false,
        };
    }

    public static bool TryValidate(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (data.SchemaVersion > RoofDefinitionDataSchema.CurrentVersion)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedFutureSchema;
            return false;
        }

        if (data.SchemaVersion is not (
                RoofDefinitionDataSchema.LegacyAbsoluteVersion or
                RoofDefinitionDataSchema.TopologyVersion or
                RoofDefinitionDataSchema.HybridLifecycleVersion or
                RoofDefinitionDataSchema.DualSlopeVersion or
                RoofDefinitionDataSchema.CurrentVersion))
        {
            return false;
        }

        if (data.Kind is not (RoofKind.SimpleGable or RoofKind.AsymmetricGable) ||
            data.SchemaVersion < RoofDefinitionDataSchema.DualSlopeVersion &&
            data.Kind != RoofKind.SimpleGable)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedRoofKind;
            return false;
        }

        if (!IsValidSlope(data.SlopeDegrees) ||
            !IsValidSlope(data.EffectiveFace1SlopeDegrees) ||
            data.Kind == RoofKind.SimpleGable &&
            Math.Abs(data.SlopeDegrees - data.EffectiveFace1SlopeDegrees) >
                SimpleGableRoofGeometryTolerance.AngularTolerance ||
            data.SchemaVersion < RoofDefinitionDataSchema.DualSlopeVersion &&
            data.Face1SlopeDegrees is { } legacyFace1 && legacyFace1 != data.SlopeDegrees)
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        if (!IsFinite(data.EaveHeightDifferenceMm) ||
            data.SchemaVersion < RoofDefinitionDataSchema.CurrentVersion &&
            Math.Abs(data.EaveHeightDifferenceMm) >
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm ||
            data.Kind == RoofKind.SimpleGable &&
            Math.Abs(data.EaveHeightDifferenceMm) >
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
        {
            error = RoofDefinitionDataDecodeError.InvalidEaveHeightDifference;
            return false;
        }

        return data.SchemaVersion == RoofDefinitionDataSchema.LegacyAbsoluteVersion
            ? TryValidateV1(data, out error)
            : TryValidateTopology(data, out error);
    }

    private static string EncodeV1(RoofDefinitionData data) =>
        string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            SimpleGableToken,
            data.SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionX!.Value.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionY!.Value.ToString("R", CultureInfo.InvariantCulture),
            data.FootprintSignature);

    private static string EncodeV2(RoofDefinitionData data)
    {
        var descriptor = data.RigidFootprint!;
        return string.Join(
            Separator.ToString(),
            RoofDefinitionDataSchema.TopologyVersion.ToString(CultureInfo.InvariantCulture),
            SimpleGableToken,
            data.SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeEdgeFamily == RoofRidgeEdgeFamily.SourceEdge01
                ? Edge01Token
                : Edge12Token,
            descriptor.VertexCount.ToString(CultureInfo.InvariantCulture),
            descriptor.SourceOrientation == RoofPolygonOrientation.Clockwise
                ? ClockwiseToken
                : CounterClockwiseToken,
            descriptor.Edge01LengthMm.ToString("R", CultureInfo.InvariantCulture),
            descriptor.Edge12LengthMm.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string EncodeV3(RoofDefinitionData data)
    {
        var topology = EncodeV2(data);
        var firstSeparator = topology.IndexOf(Separator);
        var editState = data.EditState == RoofEditState.Unlocked ? "Unlocked" : "Locked";
        var overrides = RoofGeneratedMemberOverrideCodec.Encode(data.Overrides);
        return string.Join(
            Separator.ToString(),
            RoofDefinitionDataSchema.HybridLifecycleVersion.ToString(CultureInfo.InvariantCulture),
            topology.Substring(firstSeparator + 1),
            editState,
            overrides);
    }

    private static string EncodeV4(RoofDefinitionData data)
    {
        var descriptor = data.RigidFootprint!;
        var editState = data.EditState == RoofEditState.Unlocked ? "Unlocked" : "Locked";
        return string.Join(
            Separator.ToString(),
            RoofDefinitionDataSchema.DualSlopeVersion.ToString(CultureInfo.InvariantCulture),
            data.Kind == RoofKind.AsymmetricGable ? AsymmetricGableToken : SimpleGableToken,
            data.Face0SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.EffectiveFace1SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeEdgeFamily == RoofRidgeEdgeFamily.SourceEdge01 ? Edge01Token : Edge12Token,
            descriptor.VertexCount.ToString(CultureInfo.InvariantCulture),
            descriptor.SourceOrientation == RoofPolygonOrientation.Clockwise ? ClockwiseToken : CounterClockwiseToken,
            descriptor.Edge01LengthMm.ToString("R", CultureInfo.InvariantCulture),
            descriptor.Edge12LengthMm.ToString("R", CultureInfo.InvariantCulture),
            editState,
            RoofGeneratedMemberOverrideCodec.Encode(data.Overrides));
    }

    private static string EncodeV5(RoofDefinitionData data)
    {
        var descriptor = data.RigidFootprint!;
        var editState = data.EditState == RoofEditState.Unlocked ? "Unlocked" : "Locked";
        return string.Join(
            Separator.ToString(),
            RoofDefinitionDataSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            data.Kind == RoofKind.AsymmetricGable ? AsymmetricGableToken : SimpleGableToken,
            data.Face0SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.EffectiveFace1SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.EaveHeightDifferenceMm.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeEdgeFamily == RoofRidgeEdgeFamily.SourceEdge01 ? Edge01Token : Edge12Token,
            descriptor.VertexCount.ToString(CultureInfo.InvariantCulture),
            descriptor.SourceOrientation == RoofPolygonOrientation.Clockwise ? ClockwiseToken : CounterClockwiseToken,
            descriptor.Edge01LengthMm.ToString("R", CultureInfo.InvariantCulture),
            descriptor.Edge12LengthMm.ToString("R", CultureInfo.InvariantCulture),
            editState,
            RoofGeneratedMemberOverrideCodec.Encode(data.Overrides));
    }

    private static bool TryDecodeV1(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count != 6 || !TryReadKind(fields[1], out var kind, out error) ||
            kind != RoofKind.SimpleGable)
        {
            return false;
        }

        if (!TryParseFinite(fields[2], out var slope))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        if (!TryParseFinite(fields[3], out var directionX) ||
            !TryParseFinite(fields[4], out var directionY))
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeDirection;
            return false;
        }

        var candidate = new RoofDefinitionData(
            RoofDefinitionDataSchema.LegacyAbsoluteVersion,
            RoofKind.SimpleGable,
            slope,
            directionX,
            directionY,
            fields[5],
            Face1SlopeDegrees: slope);
        return Accept(candidate, out data, out error);
    }

    private static bool TryDecodeV2(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count != 8 || !TryReadKind(fields[1], out var kind, out error) ||
            kind != RoofKind.SimpleGable)
        {
            return false;
        }

        if (!TryParseFinite(fields[2], out var slope))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        var edgeFamily = fields[3] switch
        {
            Edge01Token => RoofRidgeEdgeFamily.SourceEdge01,
            Edge12Token => RoofRidgeEdgeFamily.SourceEdge12,
            _ => RoofRidgeEdgeFamily.Undefined,
        };
        if (edgeFamily == RoofRidgeEdgeFamily.Undefined)
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily;
            return false;
        }

        if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            !TryReadOrientation(fields[5], out var orientation) ||
            !TryParseFinite(fields[6], out var edge01Length) ||
            !TryParseFinite(fields[7], out var edge12Length))
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        var candidate = new RoofDefinitionData(
            RoofDefinitionDataSchema.TopologyVersion,
            RoofKind.SimpleGable,
            slope,
            RidgeEdgeFamily: edgeFamily,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                count,
                orientation,
                edge01Length,
                edge12Length),
            Face1SlopeDegrees: slope);
        return Accept(candidate, out data, out error);
    }

    private static bool TryDecodeV3(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count < 10)
        {
            return false;
        }

        var topologyFields = new[]
        {
            RoofDefinitionDataSchema.TopologyVersion.ToString(CultureInfo.InvariantCulture),
            fields[1],
            fields[2],
            fields[3],
            fields[4],
            fields[5],
            fields[6],
            fields[7],
        };
        if (!TryDecodeV2(topologyFields, out var topology, out error) || topology is null)
        {
            return false;
        }

        var editState = fields[8] switch
        {
            "Locked" => RoofEditState.Locked,
            "Unlocked" => RoofEditState.Unlocked,
            _ => (RoofEditState?)null,
        };
        if (editState is null)
        {
            error = RoofDefinitionDataDecodeError.InvalidEditState;
            return false;
        }

        var overridePayload = fields.Count == 10
            ? fields[9]
            : string.Join(Separator.ToString(), fields.Skip(9));
        if (!RoofGeneratedMemberOverrideCodec.TryDecode(overridePayload, out var overrides, out error))
        {
            return false;
        }

        var candidate = topology with
        {
            SchemaVersion = RoofDefinitionDataSchema.HybridLifecycleVersion,
            EditState = editState.Value,
            ManualOverrides = overrides,
        };
        return Accept(candidate, out data, out error);
    }

    private static bool TryDecodeV4(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count < 11 || !TryReadKind(fields[1], out var kind, out error))
        {
            return false;
        }
        if (!TryParseFinite(fields[2], out var slope0) ||
            !TryParseFinite(fields[3], out var slope1))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        var edgeFamily = fields[4] switch
        {
            Edge01Token => RoofRidgeEdgeFamily.SourceEdge01,
            Edge12Token => RoofRidgeEdgeFamily.SourceEdge12,
            _ => RoofRidgeEdgeFamily.Undefined,
        };
        if (edgeFamily == RoofRidgeEdgeFamily.Undefined ||
            !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            !TryReadOrientation(fields[6], out var orientation) ||
            !TryParseFinite(fields[7], out var edge01Length) ||
            !TryParseFinite(fields[8], out var edge12Length))
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        var editState = fields[9] switch
        {
            "Locked" => RoofEditState.Locked,
            "Unlocked" => RoofEditState.Unlocked,
            _ => (RoofEditState?)null,
        };
        if (editState is null)
        {
            error = RoofDefinitionDataDecodeError.InvalidEditState;
            return false;
        }

        var overridePayload = fields.Count == 11
            ? fields[10]
            : string.Join(Separator.ToString(), fields.Skip(10));
        if (!RoofGeneratedMemberOverrideCodec.TryDecode(overridePayload, out var overrides, out error))
        {
            return false;
        }

        return Accept(new RoofDefinitionData(
            RoofDefinitionDataSchema.DualSlopeVersion,
            kind,
            slope0,
            RidgeEdgeFamily: edgeFamily,
            RigidFootprint: new RoofRigidFootprintDescriptor(count, orientation, edge01Length, edge12Length),
            EditState: editState.Value,
            ManualOverrides: overrides,
            Face1SlopeDegrees: slope1), out data, out error);
    }

    private static bool TryDecodeV5(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count < 12 || !TryReadKind(fields[1], out var kind, out error))
        {
            return false;
        }
        if (!TryParseFinite(fields[2], out var slope0) ||
            !TryParseFinite(fields[3], out var slope1))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }
        if (!TryParseFinite(fields[4], out var eaveHeightDifference))
        {
            error = RoofDefinitionDataDecodeError.InvalidEaveHeightDifference;
            return false;
        }

        var edgeFamily = fields[5] switch
        {
            Edge01Token => RoofRidgeEdgeFamily.SourceEdge01,
            Edge12Token => RoofRidgeEdgeFamily.SourceEdge12,
            _ => RoofRidgeEdgeFamily.Undefined,
        };
        if (edgeFamily == RoofRidgeEdgeFamily.Undefined ||
            !int.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            !TryReadOrientation(fields[7], out var orientation) ||
            !TryParseFinite(fields[8], out var edge01Length) ||
            !TryParseFinite(fields[9], out var edge12Length))
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        var editState = fields[10] switch
        {
            "Locked" => RoofEditState.Locked,
            "Unlocked" => RoofEditState.Unlocked,
            _ => (RoofEditState?)null,
        };
        if (editState is null)
        {
            error = RoofDefinitionDataDecodeError.InvalidEditState;
            return false;
        }

        var overridePayload = fields.Count == 12
            ? fields[11]
            : string.Join(Separator.ToString(), fields.Skip(11));
        if (!RoofGeneratedMemberOverrideCodec.TryDecode(overridePayload, out var overrides, out error))
        {
            return false;
        }

        return Accept(new RoofDefinitionData(
            RoofDefinitionDataSchema.CurrentVersion,
            kind,
            slope0,
            RidgeEdgeFamily: edgeFamily,
            RigidFootprint: new RoofRigidFootprintDescriptor(count, orientation, edge01Length, edge12Length),
            EditState: editState.Value,
            ManualOverrides: overrides,
            Face1SlopeDegrees: slope1,
            EaveHeightDifferenceMm: eaveHeightDifference), out data, out error);
    }

    private static bool TryValidateV1(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data.RidgeEdgeFamily is not null || data.RigidFootprint is not null)
        {
            error = RoofDefinitionDataDecodeError.MalformedPayload;
            return false;
        }

        if (!HasDefaultEditState(data))
        {
            error = RoofDefinitionDataDecodeError.MalformedPayload;
            return false;
        }

        var x = data.RidgeDirectionX;
        var y = data.RidgeDirectionY;
        var directionLength = x is not null && y is not null
            ? Math.Sqrt(x.Value * x.Value + y.Value * y.Value)
            : double.NaN;
        if (x is null || y is null ||
            !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(directionLength) ||
            Math.Abs(directionLength - 1d) > UnitDirectionTolerance ||
            x.Value < 0d || x.Value == 0d && y.Value < 0d)
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeDirection;
            return false;
        }

        if (!IsValidFootprintSignature(data.FootprintSignature))
        {
            error = RoofDefinitionDataDecodeError.InvalidFootprintSignature;
            return false;
        }

        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool TryValidateTopology(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data.RidgeDirectionX is not null ||
            data.RidgeDirectionY is not null ||
            data.FootprintSignature is not null)
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        if (data.RidgeEdgeFamily is not (
                RoofRidgeEdgeFamily.SourceEdge01 or RoofRidgeEdgeFamily.SourceEdge12))
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily;
            return false;
        }

        var descriptor = data.RigidFootprint;
        if (descriptor is null || descriptor.VertexCount != 4 ||
            descriptor.SourceOrientation is not (
                RoofPolygonOrientation.Clockwise or RoofPolygonOrientation.CounterClockwise) ||
            !IsFinite(descriptor.Edge01LengthMm) ||
            !IsFinite(descriptor.Edge12LengthMm) ||
            descriptor.Edge01LengthMm <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm ||
            descriptor.Edge12LengthMm <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm)
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        if (data.SchemaVersion == RoofDefinitionDataSchema.TopologyVersion)
        {
            if (!HasDefaultEditState(data))
            {
                error = RoofDefinitionDataDecodeError.MalformedPayload;
                return false;
            }

            error = RoofDefinitionDataDecodeError.None;
            return true;
        }

        if (data.EditState is not (RoofEditState.Locked or RoofEditState.Unlocked))
        {
            error = RoofDefinitionDataDecodeError.InvalidEditState;
            return false;
        }

        if (RoofGeneratedMemberOverrideRules.HasDuplicateKeys(data.Overrides) ||
            data.Overrides.Any(item => item is null || item.Key.StationIndex < 0))
        {
            error = RoofDefinitionDataDecodeError.InvalidManualOverride;
            return false;
        }

        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool HasDefaultEditState(RoofDefinitionData data) =>
        data.EditState == RoofEditState.Locked && data.Overrides.Count == 0;

    private static bool Accept(
        RoofDefinitionData candidate,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        if (!TryValidate(candidate, out error))
        {
            data = null;
            return false;
        }

        data = candidate;
        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool TryReadKind(
        string token,
        out RoofKind kind,
        out RoofDefinitionDataDecodeError error)
    {
        if (string.Equals(token, SimpleGableToken, StringComparison.Ordinal))
        {
            kind = RoofKind.SimpleGable;
            error = RoofDefinitionDataDecodeError.None;
            return true;
        }

        if (string.Equals(token, AsymmetricGableToken, StringComparison.Ordinal))
        {
            kind = RoofKind.AsymmetricGable;
            error = RoofDefinitionDataDecodeError.None;
            return true;
        }

        kind = default;
        error = RoofDefinitionDataDecodeError.UnsupportedRoofKind;
        return false;
    }

    private static bool TryReadOrientation(
        string token,
        out RoofPolygonOrientation orientation)
    {
        orientation = token switch
        {
            ClockwiseToken => RoofPolygonOrientation.Clockwise,
            CounterClockwiseToken => RoofPolygonOrientation.CounterClockwise,
            _ => RoofPolygonOrientation.Undefined,
        };
        return orientation != RoofPolygonOrientation.Undefined;
    }

    private static bool IsValidFootprintSignature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Contains(Separator))
        {
            return false;
        }

        var vertices = signature!.Split(';');
        if (vertices.Length < 3)
        {
            return false;
        }

        return vertices.All(vertex =>
        {
            var coordinates = vertex.Split(',');
            return coordinates.Length == 2 &&
                   TryParseFinite(coordinates[0], out _) &&
                   TryParseFinite(coordinates[1], out _);
        });
    }

    private static bool TryParseFinite(string value, out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) && IsFinite(result);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsValidSlope(double value) =>
        IsFinite(value) &&
        value > SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees &&
        value < SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees;
}
