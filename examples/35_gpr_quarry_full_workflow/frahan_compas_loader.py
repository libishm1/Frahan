# Frahan -> COMPAS bridge loader.  pip install compas   (optional: compas_assembly, compas_fab)
import json
from compas.geometry import Frame, Point, Vector
from compas.datastructures import Mesh


def load_frahan(path):
    """Return {frames, blocks (compas Mesh), interfaces} from a Frahan bridge JSON."""
    d = json.load(open(path))
    frames = [Frame(Point(*f['point']), Vector(*f['xaxis']), Vector(*f['yaxis']))
              for f in d.get('frames', [])]
    blocks = [Mesh.from_vertices_and_faces(b['vertices'], b['faces'])
              for b in d.get('blocks', [])]
    return {'frames': frames, 'blocks': blocks, 'interfaces': d.get('interfaces', [])}


def to_assembly(path):
    """Build a compas_assembly.Assembly (pip install compas_assembly)."""
    from compas_assembly.datastructures import Assembly, Block
    d = json.load(open(path))
    a = Assembly()
    for b in d.get('blocks', []):
        a.add_block(Block.from_vertices_and_faces(b['vertices'], b['faces']))
    return a  # feed to compas_cra for the equilibrium solve


if __name__ == '__main__':
    import sys
    data = load_frahan(sys.argv[1])
    print(len(data['blocks']), 'blocks,', len(data['frames']), 'frames,', len(data['interfaces']), 'interfaces')
