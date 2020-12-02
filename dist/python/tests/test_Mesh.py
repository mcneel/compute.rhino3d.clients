import compute_rhino3d.Util
import compute_rhino3d.Mesh
import rhino3dm
import requests_mock
from . import objects

compute_rhino3d.Util.url = 'http://test.com/'

def test_mesh():
    with requests_mock.Mocker() as m:
        m.post('http://test.com/rhino/geometry/mesh/createfrombrep-brep', json=objects.mesh)
        brep = rhino3dm.Sphere(rhino3dm.Point3d(0,0,0), 12).ToBrep()
        meshes = compute_rhino3d.Mesh.CreateFromBrep(brep)
        assert len(meshes) == 1
        assert len(meshes[0].Vertices) == 561