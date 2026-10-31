"""Small numerical checks for the engraving maps, independent of Unity."""
import unittest
import numpy as np
from build_maps import make_maps, sparse_chips, compose_layout


class EngravingMapsTests(unittest.TestCase):
    def test_empty_surface_is_flat_and_unmarked(self):
        h, normal, packed = make_maps(np.zeros((32, 48), np.float32), 3, .009, 4)
        self.assertTrue(np.all(h == 0))
        np.testing.assert_allclose(normal, np.broadcast_to([0, 0, 1], normal.shape))
        self.assertTrue(np.all(packed[..., 0] == 0))
        self.assertTrue(np.all(packed[..., 1] == 1))

    def test_groove_is_inward_with_correct_light_response(self):
        mask = np.zeros((48, 64), np.float32)
        mask[8:40, 24:40] = 1
        h, normal, packed = make_maps(mask, 3, .009, 4)
        self.assertAlmostEqual(float(h[24, 32]), -.009, places=6)
        self.assertGreater(normal[24, 24, 0], 0)
        self.assertLess(normal[24, 39, 0], 0)
        self.assertLess(normal[8, 32, 1], 0)
        self.assertGreater(normal[39, 32, 1], 0)
        self.assertLess(packed[24, 32, 1], 1)
        self.assertTrue(np.all(h[mask == 0] == 0))
        np.testing.assert_allclose(np.linalg.norm(normal, axis=-1), 1, atol=1e-6)

    def test_rebuild_is_identical(self):
        mask = np.zeros((32, 32), np.float32)
        mask[5:27, 12:20] = 1
        for a, b in zip(make_maps(mask, 3, .009, 4), make_maps(mask, 3, .009, 4)):
            np.testing.assert_array_equal(a, b)

    def test_invalid_dimensions_are_rejected(self):
        with self.assertRaises(ValueError):
            make_maps(np.zeros((10, 10)), 0, .009, 4)

    def test_chipping_is_sparse_repeatable_and_switchable(self):
        base=np.zeros((192,192),np.float32);base[30:160,70:95]=1
        chipped=np.zeros_like(base);chipped[27:163,67:98]=1
        np.testing.assert_array_equal(sparse_chips(base,chipped,0,20261029),base)
        a=sparse_chips(base,chipped,.25,20261029)
        np.testing.assert_array_equal(a,sparse_chips(base,chipped,.25,20261029))
        self.assertTrue(np.any(a!=base))
        self.assertLess(np.count_nonzero(a!=base),np.count_nonzero(chipped!=base)*.5)
        self.assertTrue(np.all(a[:,120:]==0))

    def test_layout_preserves_art_and_places_glyphs_above_below(self):
        art=np.ones((40,60),np.float32)*.5
        rain=np.zeros((12,10),np.float32);rain[2:8,3:7]=1
        water=np.zeros_like(rain);water[4:10,1:5]=1
        layout=compose_layout(art,rain,water)
        np.testing.assert_array_equal(layout[10:50,:],art)
        self.assertGreater(layout[:10,:].sum(),0)
        self.assertGreater(layout[50:,:].sum(),0)


if __name__ == '__main__':
    unittest.main()
