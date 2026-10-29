import unittest
import numpy as np
from prepare import match_glyph, topology, bounded_chips, distance, make_maps

class ProductionTests(unittest.TestCase):
    def test_glyph_matches_picture_preserving_separate_strokes(self):
        a=np.zeros((100,100),np.float32);a[20:80,20:26]=1;a[20:80,65:71]=1
        b,metrics=match_glyph(a,14)
        self.assertGreaterEqual(metrics['after'],14)
        self.assertEqual(topology(a),topology(b))
        self.assertEqual(topology(b),(2,0))
    def test_rejects_thickening_that_changes_identity(self):
        a=np.zeros((50,50),np.float32);a[10:40,10:13]=1;a[10:40,17:20]=1
        with self.assertRaises(ValueError):match_glyph(a,18)
    def test_chip_candidate_cannot_erase_core_or_add_distant_lines(self):
        a=np.zeros((160,160),np.float32);a[20:140,40:60]=1
        candidate=1-a
        b=bounded_chips(a,candidate,42)
        np.testing.assert_array_equal(b,bounded_chips(a,candidate,42))
        np.testing.assert_array_equal(a[distance(a>.5,8)>2],b[distance(a>.5,8)>2])
        self.assertEqual(float(b[:,100:].max()),0)
        self.assertGreater(np.count_nonzero(a!=b),0)
    def test_rectangular_physical_spacing(self):
        a=np.zeros((60,60),np.float32);a[20:40,20:40]=1
        _,n,_=make_maps(a,1,.08,4,height_m=2)
        self.assertGreater(abs(float(n[30,20,0])),abs(float(n[20,30,1])))
        with self.assertRaises(ValueError):make_maps(a,1,.08,4,height_m=0)

if __name__=='__main__':unittest.main()
