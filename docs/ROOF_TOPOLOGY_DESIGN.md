# Shared roof topology foundation

This is a CAD-neutral geometry foundation, not an AutoCAD acceptance
record. Roof kinds remain distinct user presets. No official numeric stage is
assigned here. The stable gable/asymmetric-gable and monopitch production paths,
their signatures, codecs and lifecycle remain unchanged.

## Algorithm and implemented boundary

`RoofTopologySolver` accepts one `RoofFootprintInput` (or an already validated
`RoofFootprint`) and a uniform pitch strictly between 0 and 90 degrees. The input
contains one closed outer polygon, straight edges, no holes or separate islands.
The existing closure, duplicate-edge, collinearity and self-intersection rules are
reused. Validation and calculations run in a local translated frame to avoid the
existing absolute-coordinate area sum losing precision at large WCS offsets.
The existing production footprint validator is not modified.

For a convex polygon P and inward unit normal n_i at eave origin a_i, define
`d_i(p) = n_i · (p - a_i)`. The roof is the lower envelope
`z(p) = tan(pitch) * min_i d_i(p)`. Face i is P clipped by every half-plane
`d_i(p) <= d_j(p)`. This is an in-house deterministic half-plane construction of
the convex uniform-speed straight skeleton, not a rectangle decomposition.
All constraints are processed in canonical source-edge order. Clipped vertices
retain their defining source constraints; intersections use the best-conditioned
pair of equivalent normalized bisectors, so near-collinear eaves do not create
inconsistent event positions on adjacent faces through interpolation cancellation.
There is no mutable
event queue whose tie ordering could select a different symmetric result.

This equivalence is specific to convex polygons. The
[CGAL straight-skeleton manual](https://doc.cgal.org/latest/Straight_skeleton_2/index.html)
describes inward-offset wavefronts and their equivalence to the medial axis/Voronoi
diagram for convex polygons. It is an algorithm reference, not a dependency.
No package, native binary, network service or new framework is introduced.
Core remains `netstandard2.0` with no package references.

Rectangles derive `h = W/2 * tan(pitch)` and `ridgeLength = L-W` from the same
envelope as other convex polygons. They no longer drive the topology architecture.
Regular polygons produce a single simultaneous apex; irregular convex polygons
may have multiple internal ridges, with varying heights and branch points.

Concave inputs route to `ConcaveRoofTopologySolver`; convex inputs retain the
published clipping backend and assembler unchanged. Both return the same immutable
graph. Extending the supporting-line minimum to concave footprints would introduce
false negative distances and incorrect face ownership; it is never a fallback.

## Result and canonicalization

`RoofTopology` is an immutable indexed graph, independent of RoofKind:

- Nodes store WCS XY and local-datum height once. Initial contour nodes are
  separate from internal nodes; `BoundaryVertexCount` is independent of face count.
- Shared edges reference endpoint indices, incident face indices and an
  Eave/Hip/Ridge/Valley role. A `CoplanarSeam` additionally preserves distinct source
  ownership when collinear active fronts meet after a split. It is not a physical
  ridge and is excluded from the Hip wrapper's `Ridges`. Existing edge-role numeric
  values are unchanged. Internal degree is not restricted to three.
- Each current all-eave face references its canonical source edge and a connected
  CCW node cycle beginning with that eave. No copied point coordinates or graph
  mutation APIs are exposed. A face's other edges are recovered from the shared graph.
- Face order follows canonical source edges. Eaves come first in edge order,
  followed by skeleton edges sorted by endpoint IDs. Internal nodes are ordered
  lexicographically in the local frame. These are result-local indices, not durable
  identities for future editing or persistence.
- Hip views retain rectangle presentation ordering and single-ridge/apex convenience
  accessors; multiple ridges are exposed through `Ridges` and the shared topology.
  `Ridge` is null when there is no unique ridge. Actual face pitch is derived from
  its boundary; no duplicated solver geometry is stored by the Hip face views.

Convex clipping arithmetic uses `max(1e-12 mm, longestEdge * 1e-14)` to recognize numerical
zero. Event coalescence uses the existing roof length policy
`tau = max(1e-6 mm, longestEdge * 1e-10)`. Event candidates within tau are clustered
together; a transitive cluster whose diameter exceeds tau is rejected rather than
silently swallowing a chain of distinct small features. Source corners remain
anchored. Near-square internal ridge endpoints therefore collapse to their common
center within tau. The actual footprint is retained, so the two opposite pitch
pairs can differ by the tolerance-sized dimensional discrepancy. Exact squares
retain uniform pitch. Ambiguous topology returns `NumericallyUnresolvedTopology`.

The convex assembler verifies eave ownership, two-sided internal incidence with opposite
face traversal, connected skeleton tree, boundary leaves, internal degree >= 3,
nonzero finite segments, upward planar faces and footprint area coverage. Arithmetic
limits are reported as failure rather than returning a partially valid graph.
This floating-point implementation does not claim exact-predicate guarantees for
arbitrarily ill-conditioned geometry. The clipping backend is suitable for building
footprints; event clustering also compares nearby candidate pairs and is not a
large-mesh performance contract.

Signatures include coordinates and connectivity with invariant formatting, six
coordinate decimals and ten pitch decimals. They identify WCS results, so rotation
or translation intentionally changes a signature. CW/CCW and cyclic starts of the
same geometric input produce identical signatures. Rigid-transform tests compare
transformed node/edge/face geometry, allowing the canonical WCS ordering to change.
A quantized key is not an approximate-distance predicate; values can cross a
rounding-bin boundary. No persistence or lifecycle decision uses this new key yet.

## Ridge direction and shared presets

The topology engine has no global ridge-direction input. For arbitrary polygons,
all ridge and valley branches must arise from the footprint and boundary conditions.
`HipRoofGeometrySolver` keeps a supplied rectangle direction only as a legacy
constraint: it must align with the major edge family (either family for collapse).
An omitted direction is inferred. Nonrectangular Hip inputs ignore that legacy
field; changing it cannot change their topology. The required `IRoofGeometry`
orientation property is a presentation axis in the wrapper, not a general ridge
direction. The existing gable solver is consulted only to retain the rectangle
wrapper's old direction validation/presentation; it does not compute polygon faces.

Future Sedlová and mixed presets should supply source-edge/branch boundary conditions
to the same topology entry point and consume the same shared graph. A Gable
termination makes a branch reach the contour and changes the incident roof-face
boundaries. It cannot be implemented correctly by moving a ridge endpoint while
keeping its old faces. Boundary conditions for stationary/gable fronts, boundary
edge roles and split/merge provenance will be introduced when that solver contract
is implemented; this slice adds no unused termination classes. The present backend
is explicitly all-eave, not a complete mixed-termination solver. Stable production
SimpleGable/Monopitch dispatch remains on their current implementations.

## Concave kinetic wavefront

This is one in-house inward-offset process for all concave fixtures, with no
rectangle decomposition or shape-name routing. At time t, active source edge i
lies on `n_i dot (p - a_i) = t`; roof height is `t * tan(pitch)`. The source unit
normal and original canonical edge ID never change. Active spans may split while
retaining the same source ID. Time is offset distance in mm, independent of pitch.

`WavefrontLoop` stores a cyclic sequence with predecessor/successor access.
`WavefrontVertex` stores a stable internal ID, its previous/next source IDs, birth
time, position, velocity, trace anchor and reflex/collinear classification.
These are solver-local identities, not CAD IDs. Multiple loops continue after a
split; source ownership remains global. Vertex motion is linear between events.
For two incident unit normals, velocity is `(n_a + n_b) / (|n_a + n_b|^2 / 2)`.
This avoids cancellation from intersecting almost parallel moving lines. Same-
direction collinear descendants move along their common normal from the event
anchor and trace a coplanar ownership seam. Opposite parallel fronts cannot form
a continuing vertex and must disappear in the terminal-strip resolution.

`WavefrontEvents` recomputes candidates from the current live loops after each
batch; there are no stale queue entries. An edge event solves the zero of its
signed active span length and verifies that its moving endpoints coincide. A split
event solves a reflex vertex's contact with a nonadjacent moving source line,
then checks the finite active span and its orientation at that time. Candidates
include time, type, vertex and edge-endpoint IDs and source ownership. Ordering is
time, type, source ID, vertex ID and edge-start ID; ordering does not choose the
mutation of a simultaneous contact. Pitch and global ridge direction are absent.

### Simultaneous events and active mutations

Use `tau = max(1e-6 mm, longestEdge * 1e-10)` and arithmetic zero
`eta = max(1e-12 mm, longestEdge * 1e-14)`. A time batch extends from its earliest
candidate by at most `tau / (2 * max(1, maximumActiveVertexSpeed))`. This bounds
spatial travel even at acute corners. Resolve at the midpoint of the batch's
earliest/latest times. No raw floating-point time equality is used.

`WavefrontEventResolver` resolves the whole contact graph at that time:

1. Advance active vertices and cluster positions within tau. Each cluster must
   have diameter <= tau, including transitive members; the bounding-box midpoint
   is its representative. A wider chained cluster is ambiguous and fails.
2. Split every active span at all contacting vertex clusters, including vertex-
   vertex, endpoint split and several simultaneous reflex contacts. The spatial
   contact graph may include a further near-time contact already within tau.
   Every candidate in the time batch must appear in this graph, or the solve fails.
3. Remove zero-length spans. Cancel oppositely directed coincident intervals as
   whole terminal strips, emitting a ridge with both source owners. This handles
   positive-length simultaneous collapses in equal-width L/U/T. Multiplicity or
   inconsistent orientation fails; no event is arbitrarily selected and discarded.
4. End all involved old vertex traces at the corresponding shared event node.
   For each remaining directed interval, select the outgoing ray immediately
   clockwise from the reversed incoming ray. Require a bijection over the entire
   contact star. Tied rays, invalid incidence or nonpositive resulting loops fail.
5. Traverse all resulting cycles and create their continuing vertices. Unaffected
   vertices keep their original anchors and identities, avoiding artificial
   degree-two skeleton nodes. Each new loop remains active until it disappears.

No epsilon-time stepping, random perturbation or rectangle stitching is used.
A conservative quadratic event-budget guard and absence of a future candidate
convert nonprogress into `NumericallyUnresolvedTopology`, without partial output.

### Face ownership and classification

A traced vertex segment carries its two incident original source-face IDs: its
previous source is on the left and next source on the right of the trace. Terminal
strip ridges carry the two cancelling owners. `WavefrontTopologyAssembler` combines
these directed incidences with the original eaves, requires one complete CCW cycle
per source, and canonicalizes nodes, edges and face cycles into `RoofTopology`.
Source i is always face i; neither split order nor loop identity reassigns it.
The convex cell builder is not reused because it assumes a positive triangle fan.

Exact edge roles are:

- `Eave`: original canonical contour edge, one incident face.
- `Hip`: trace of an initial convex corner into the interior.
- `Valley`: trace of a reflex active corner, including a reflex continuation after
  an event. It follows the evolving wavefront, not only the initial corner flag.
- `Ridge`: nonreflex interior branch or the common interval of a disappearing
  strip, with two noncoplanar incident faces.
- `CoplanarSeam`: continuing ownership boundary of distinct but coplanar sources.
  This occurs, for example, after the connector of a dumbbell footprint disappears.
  Keeping it prevents merging or losing the two original faces without falsely
  adding a physical ridge or valley.

The integrity validator independently checks the sign of the incident plane
gradient jump across each directed edge: a local maximum is a valley, a local
minimum is a hip/ridge according to contour incidence, and coincident planes form
a seam. Classification therefore has a check independent of the event trace flag.

### Validation and numerical boundary

`RoofTopologyIntegrity` validates in the translated local frame before publishing:
finite nodes/heights/lengths, valid node and face indices, one-sided eaves and
oppositely traversed two-sided internal incidence, no duplicate edges, closed
nonrepeating source cycles, connected skeleton tree, contour degree one and
internal degree at least three. It checks every face against its source offset
plane and uniform pitch. Concave face areas use signed sums, not an assumed convex
triangulation. Positive face areas must sum to the footprint area within
`tau * perimeter`. Nodes and edge midpoints must lie inside/on the footprint.
Nonincident crossings, touching edges and overlapping incident rays are rejected.
The validated immutable graph must also yield a finite canonical signature.

Vertex clusters, source-plane residuals and projected internal lengths use tau;
height residuals use `tau * max(1, tan(pitch))`; direction predicates use the
existing angular tolerance 1e-10. An internal segment <= tau is unresolved, not a
silently erased feature. Consequently a nearly symmetric shape can be resolvable
as a coalesced event, unresolved when it implies a sub-tolerance branch, and
resolvable again once that branch is sufficiently separated. Tests explicitly
exercise that failure interval and equivalent CW/start/rotation representations.
`ConcaveWavefrontNotImplemented` remains an unused legacy enum value; this backend
never returns it. Invalid polygon inputs retain the original validation errors.

Tests cover analytical equal-width L/U/T event positions, asymmetric U/T,
nonorthogonal reflex polygons, a stepped polygon, shallow reflex, narrow notch,
multiple reflex vertices and a dumbbell whose two surviving loops collapse at
different later times. Checks include plane pitch, complete area, containment,
noncrossing/connected incidence, source ownership, coplanar seams, symmetric high-
degree nodes, winding/start signatures, culture and rigid transforms. Analytical
L/U/T and dumbbell offsets are independent geometric checks. No external library
oracle or exact-arithmetic proof is claimed. Floating-point support is limited by
the stated tolerance and tested simple-polygon contract, not a guarantee for every
ill-conditioned polygon. Recomputed event and contact searches are intentionally
simple and intended for building footprints, not a large-mesh performance promise.

The mathematical event model is consistent with the
[straight-skeleton builder event descriptions](https://doc.cgal.org/latest/Straight_skeleton_2/classCGAL_1_1Straight__skeleton__builder__2.html).
That document is a reference, not an installed dependency. No external package,
native binary, filesystem/network access or framework change is needed by the
solver. Holes, islands, curved edges, self-intersections, nonplanar input and
per-edge pitch remain outside this contract. Future stationary/Gable/HalfHip
conditions are not implemented and must extend the shared graph semantics.

## Timber/settings and persistence boundaries

The current rafter workflow reads `AutomaticRafterPreferences` from
`SettingsUiPreferencesStore`; absent remembered values, `RoofRafterPreferences`
currently supplies 900 mm. `RafterLayoutParameters.MaximumSpacingMm` is already a
neutral input consumed by the layout solver. A future central Settings value can be
resolved by the application layer and passed through that input without coupling
geometry to a settings store. Confirmed future default: `RafterSpacingMm = 500 mm`.
Changing the existing 900 mm fallback and its precedence/migration belongs to that
future Settings/generation slice, not to this geometry change.

Tiny terminal-hip member suppression is a configurable timber-generation policy.
The example 200 mm is not a decided default. Neither 200 nor 500 mm occurs as a
geometry rule. No rafters, central settings fields or schema changes are added here.

`RoofKind.Hip = 4` preserves values 1/2/3 and is still refused by the existing
roof-definition codec and persistence writer. Product version and roof/timber/drawing
schemas remain unchanged. There is no AutoCAD preview, UI, entity, group, annotation,
import, licensing, multi-CAD or HOST acceptance work in this foundation.
