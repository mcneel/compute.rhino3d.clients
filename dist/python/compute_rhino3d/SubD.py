from . import Util
try:
    from itertools import izip as zip # python 2
except ImportError:
    pass # python 3


def JoinSubDs(subdsToJoin, tolerance, joinedEdgesAreCreases, multiple=False):
    """
    Joins an enumeration of SubDs to form as few as possible resulting SubDs.
    There may be more than one SubD in the result array.

    Args:
        subdsToJoin (IEnumerable<SubD>): An enumeration of SubDs to join.
        tolerance (double): The join tolerance.
        joinedEdgesAreCreases (bool): If true, merged boundary edges will be creases.
            If false, merged boundary edges will be smooth.
    """
    url = "rhino/geometry/subd/joinsubds-subdarray_double_bool"
    if multiple: url += "?multiple=true"
    args = [subdsToJoin, tolerance, joinedEdgesAreCreases]
    if multiple: args = list(zip(subdsToJoin, tolerance, joinedEdgesAreCreases))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def ToBrep(thisSubD, options, multiple=False):
    """
    Create a Brep based on this SubD geometry.

    Args:
        options (SubDToBrepOptions): The SubD to Brep conversion options. Use SubDToBrepOptions.Default
            for sensible defaults. Currently, these return unpacked faces
            and locally-G1 vertices in the output Brep.

    Returns:
        Brep: A new Brep if successful, or None on failure.
    """
    url = "rhino/geometry/subd/tobrep-subd_subdtobrepoptions"
    if multiple: url += "?multiple=true"
    args = [thisSubD, options]
    if multiple: args = list(zip(thisSubD, options))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def ToBrep1(thisSubD, multiple=False):
    """
    Create a Brep based on this SubD geometry, based on SubDToBrepOptions.Default options.

    Returns:
        Brep: A new Brep if successful, or None on failure.
    """
    url = "rhino/geometry/subd/tobrep-subd"
    if multiple: url += "?multiple=true"
    args = [thisSubD]
    if multiple: args = [[item] for item in thisSubD]
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromMesh(mesh, multiple=False):
    """
    Create a new SubD from a mesh.

    Args:
        mesh (Mesh): The input mesh.

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/createfrommesh-mesh"
    if multiple: url += "?multiple=true"
    args = [mesh]
    if multiple: args = [[item] for item in mesh]
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromMesh1(mesh, options, multiple=False):
    """
    Create a new SubD from a mesh.

    Args:
        mesh (Mesh): The input mesh.
        options (SubDCreationOptions): The SubD creation options.

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/createfrommesh-mesh_subdcreationoptions"
    if multiple: url += "?multiple=true"
    args = [mesh, options]
    if multiple: args = list(zip(mesh, options))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromSurface(surface, method, corners, multiple=False):
    """
    Create a SubD that approximates the surface. If the surface is a SubD
    friendly NURBS surface and withCorners is true, then the SubD and input
    surface will have the same geometry.

    Args:
        method (SubDFromSurfaceMethods): Selects the method used to calculate the SubD.
        corners (bool): If the surface is open, then the corner vertices with be tagged as
            VertexTagCorner. This makes the resulting SubD have sharp corners to
            match the appearance of the input surface.
    """
    url = "rhino/geometry/subd/createfromsurface-surface_subdfromsurfacemethods_bool"
    if multiple: url += "?multiple=true"
    args = [surface, method, corners]
    if multiple: args = list(zip(surface, method, corners))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def Offset(thisSubD, distance, solidify, multiple=False):
    """
    Makes a new SubD with vertices offset at distance in the direction of the control net vertex normals.
    Optionally, based on the value of solidify, adds the input SubD and a ribbon of faces along any naked edges.

    Args:
        distance (double): The distance to offset.
        solidify (bool): True if the output SubD should be turned into a closed SubD.

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/offset-subd_double_bool"
    if multiple: url += "?multiple=true"
    args = [thisSubD, distance, solidify]
    if multiple: args = list(zip(thisSubD, distance, solidify))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromLoft(curves, closed, addCorners, addCreases, divisions, multiple=False):
    """
    Creates a SubD lofted through shape curves.

    Args:
        curves (IEnumerable<NurbsCurve>): An enumeration of SubD-friendly NURBS curves to loft through.
        closed (bool): Creates a SubD that is closed in the lofting direction. Must have three or more shape curves.
        addCorners (bool): With open curves, adds creased vertices to the SubD at both ends of the first and last curves.
        addCreases (bool): With kinked curves, adds creased edges to the SubD along the kinks.
        divisions (int): The segment number between adjacent input curves.

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/createfromloft-nurbscurvearray_bool_bool_bool_int"
    if multiple: url += "?multiple=true"
    args = [curves, closed, addCorners, addCreases, divisions]
    if multiple: args = list(zip(curves, closed, addCorners, addCreases, divisions))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromSweep(rail1, shapes, closed, addCorners, roadlikeFrame, roadlikeNormal, multiple=False):
    """
    Fits a SubD through a series of profile curves that define the SubD cross-sections and one curve that defines a SubD edge.

    Args:
        rail1 (NurbsCurve): A SubD-friendly NURBS curve to sweep along.
        shapes (IEnumerable<NurbsCurve>): An enumeration of SubD-friendly NURBS curves to sweep through.
        closed (bool): Creates a SubD that is closed in the rail curve direction.
        addCorners (bool): With open curves, adds creased vertices to the SubD at both ends of the first and last curves.
        roadlikeFrame (bool): Determines how sweep frame rotations are calculated.
            If False (Freeform), frame are propogated based on a refrence direction taken from the rail curve curvature direction.
            If True (Roadlike), frame rotations are calculated based on a vector supplied in "roadlikeNormal" and the world coordinate system.
        roadlikeNormal (Vector3d): If roadlikeFrame = true, provide 3D vector used to calculate the frame rotations for sweep shapes.
            If roadlikeFrame = false, then pass .

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/createfromsweep-nurbscurve_nurbscurvearray_bool_bool_bool_vector3d"
    if multiple: url += "?multiple=true"
    args = [rail1, shapes, closed, addCorners, roadlikeFrame, roadlikeNormal]
    if multiple: args = list(zip(rail1, shapes, closed, addCorners, roadlikeFrame, roadlikeNormal))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def CreateFromSweep1(rail1, rail2, shapes, closed, addCorners, multiple=False):
    """
    Fits a SubD through a series of profile curves that define the SubD cross-sections and two curves that defines SubD edges.

    Args:
        rail1 (NurbsCurve): The first SubD-friendly NURBS curve to sweep along.
        rail2 (NurbsCurve): The second SubD-friendly NURBS curve to sweep along.
        shapes (IEnumerable<NurbsCurve>): An enumeration of SubD-friendly NURBS curves to sweep through.
        closed (bool): Creates a SubD that is closed in the rail curve direction.
        addCorners (bool): With open curves, adds creased vertices to the SubD at both ends of the first and last curves.

    Returns:
        SubD: A new SubD if successful, or None on failure.
    """
    url = "rhino/geometry/subd/createfromsweep-nurbscurve_nurbscurve_nurbscurvearray_bool_bool"
    if multiple: url += "?multiple=true"
    args = [rail1, rail2, shapes, closed, addCorners]
    if multiple: args = list(zip(rail1, rail2, shapes, closed, addCorners))
    response = Util.ComputeFetch(url, args)
    response = Util.DecodeToCommonObject(response)
    return response


def MergeAllCoplanarFaces(thisSubD, tolerance, multiple=False):
    """
    Merges adjacent coplanar faces into single faces.

    Args:
        tolerance (double): Tolerance for determining when edges are adjacent.
            When in doubt, use the document's ModelAbsoluteTolerance property.

    Returns:
        bool: True if faces were merged, False if no faces were merged.
    """
    url = "rhino/geometry/subd/mergeallcoplanarfaces-subd_double"
    if multiple: url += "?multiple=true"
    args = [thisSubD, tolerance]
    if multiple: args = list(zip(thisSubD, tolerance))
    response = Util.ComputeFetch(url, args)
    return response


def MergeAllCoplanarFaces1(thisSubD, tolerance, angleTolerance, multiple=False):
    """
    Merges adjacent coplanar faces into single faces.

    Args:
        tolerance (double): Tolerance for determining when edges are adjacent.
            When in doubt, use the document's ModelAbsoluteTolerance property.
        angleTolerance (double): Angle tolerance, in radians, for determining when faces are parallel.
            When in doubt, use the document's ModelAngleToleranceRadians property.

    Returns:
        bool: True if faces were merged, False if no faces were merged.
    """
    url = "rhino/geometry/subd/mergeallcoplanarfaces-subd_double_double"
    if multiple: url += "?multiple=true"
    args = [thisSubD, tolerance, angleTolerance]
    if multiple: args = list(zip(thisSubD, tolerance, angleTolerance))
    response = Util.ComputeFetch(url, args)
    return response


def InterpolateSurfacePoints(thisSubD, surfacePoints, multiple=False):
    """
    Modifies the SubD so that the SubD vertex limit surface points are
    equal to surface_points[]

    Args:
        surfacePoints (Point3d[]): point for limit surface to interpolate. surface_points[i] is the
            location for the i-th vertex returned by SubVertexIterator vit(this)
    """
    url = "rhino/geometry/subd/interpolatesurfacepoints-subd_point3darray"
    if multiple: url += "?multiple=true"
    args = [thisSubD, surfacePoints]
    if multiple: args = list(zip(thisSubD, surfacePoints))
    response = Util.ComputeFetch(url, args)
    return response

