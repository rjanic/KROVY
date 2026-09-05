# Shared roof topology foundation

This is an unpublished CAD-neutral geometry foundation, not an AutoCAD acceptance
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

Concave L/U/T inputs are valid simple polygons but return
`ConcaveWavefrontNotImplemented`, with no partial result. Extending the supporting
line minimum to concave footprints would introduce false negative distances and
incorrect face ownership; it must never be used as a fallback.

## Result and canonicalization

`RoofTopology` is an immutable indexed graph, independent of RoofKind:

- Nodes store WCS XY and local-datum height once. Initial contour nodes are
  separate from internal nodes; `BoundaryVertexCount` is independent of face count.
- Shared edges reference endpoint indices, incident face indices and an
  Eave/Hip/Ridge/Valley role. Internal degree is not restricted to three.
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

Clipping arithmetic uses `max(1e-12 mm, longestEdge * 1e-14)` to recognize numerical
zero. Event coalescence uses the existing roof length policy
`tau = max(1e-6 mm, longestEdge * 1e-10)`. Event candidates within tau are clustered
together; a transitive cluster whose diameter exceeds tau is rejected rather than
silently swallowing a chain of distinct small features. Source corners remain
anchored. Near-square internal ridge endpoints therefore collapse to their common
center within tau. The actual footprint is retained, so the two opposite pitch
pairs can differ by the tolerance-sized dimensional discrepancy. Exact squares
retain uniform pitch. Ambiguous topology returns `NumericallyUnresolvedTopology`.

The assembler verifies eave ownership, two-sided internal incidence with opposite
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

## Concave phase: precise missing work

The missing component is a robust kinetic wavefront backend, not a special-case
L/U/T patch. It can return the same graph and does not require a schema change:

1. Track active directed contour loops, source-edge provenance and offset time.
2. Compute edge-collapse events and reflex-vertex impacts against nonadjacent
   active edge spans. Supporting-line intersection alone is not a valid split event.
3. Handle reflex/vertex collisions and batches of simultaneous events by resolving
   the whole local incidence graph at a common time; invalidate stale event candidates.
4. After a split, continue each resulting wavefront loop independently while retaining
   global source-face ownership. Stitch each source face into a continuous cycle.
5. Trace convex and reflex branches into hips/valleys, with ridges and internal
   branch nodes. Preserve classification beyond split/vertex events; source reflex
   classification alone is insufficient for every later wavefront event.
6. Validate L/U/T and asymmetric variants, unequal arm widths, narrow corridors,
   orthogonal simultaneous collapses, rotations, winding/start variants, no crossings,
   face coverage and physical pitch. Use independently calculated fixtures and a
   trusted reference oracle before claiming robust arbitrary concave support.

These event/control-flow requirements follow the
[straight-skeleton builder event descriptions](https://doc.cgal.org/latest/Straight_skeleton_2/classCGAL_1_1Straight__skeleton__builder__2.html).
They have not been implemented or accepted here. An in-house backend remains an
option; this task does not conclude that an external package is required. If a
library is proposed later, its exact version, license, `netstandard2.0` compatibility,
native/offline requirements and deterministic behavior require review before adding
it. No library decision is concealed behind a concave approximation.

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
