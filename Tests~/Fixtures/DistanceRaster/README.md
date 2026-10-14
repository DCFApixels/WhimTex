# Frozen raster corner inputs

These five 512x512 RGBA PNGs are original raster **inputs**, not SDF output goldens.
Captured read-only from the user's unsaved thirteen-ray Star layer on 2026-10-13,
before the corresponding corner fixes. The original document was not edited or saved.
No vector data is supplied to production in these tests.

| Input | Inner ratio | Pixel-index center | Outer radius | SHA-256 |
| --- | --- | --- | --- | --- |
| star-13-inner-0368.png | .368 | 248.203460693, 255.948944092 | 89.7093963623 | CB725C3A9F28D6B9511D98BFF2861730E4A899E8899F3347B779B51270F3045A |
| star-13-inner-0413.png | .413 | 246.610931396, 251.967605591 | 89.7093963623 | 745E4920F392354A6713CD30E326AD759128726C899E99D799C4D0FE727F5797 |
| star-13-inner-0364.png | .364 | 259.329071045, 250.861679077 | 89.7093963623 | EE0BDE8C29364AED0D13FEB5A545F8032F9A1B188130AA5B02C7C7C53BE131AD |
| star-13-inner-0364-phase2.png | .364 | 253.799438477, 254.179458618 | 89.7093963623 | F52290C1515C7688EBF704D1FAB9DCA2DB8044D949673AFEEE34A2EB9A499C95 |
| star-13-inner-0364-phase3-known-failure.png | .364 | 245.040496826, 251.790664673 | 89.7093963623 | 98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC |

`distance-raster-captured-v1` authenticates the input bytes before decoding and
checks independent finite and between-ray angular envelopes at moderate thresholds,
with Sharp Miter Limit 64. Above-cap acute tips now have finite-distance expectations;
ordinary supported tips retain angular expectations in the wedge suite. The third input reproduced an unrelated lower ray from a
tiny AA component at Threshold 128 and Miter Limit 34.49. The fourth reproduced
an internal bevel at a concave corner next to a 126-alpha subpixel hole. Its test
requires a shared supported corner without erasing the hole's original crossings.
It does not compare
against images rendered by the current implementation. Original raster coverage is
not an exact vector contour; the declared ray budgets account for that uncertainty.
Never regenerate or replace these source inputs to make a regression pass.

## Fifth input: original false-ray regression

`star-13-inner-0364-phase3-known-failure.png` is a fifth original input retained
after further algorithm changes were stopped at the user's request. The filename
preserves that provenance. It now belongs to `RunCaptured` and the independent
`distance-safety-v1` regression; it is not an output golden.
SHA-256: `98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC`.
Center (245.040496826,251.790664673), radius 89.7093963623, inner ratio .364.
At Threshold 128 and Sharp Miter Limit 34.49, the previous implementation
produced an unrelated left ray. At (20,238), its scalar distance was 6.626863 px,
versus 58.078659 px from the independent ideal polygon. Two subpixel edges
inside a large merged contour create the false angular join; the whole-contour
perimeter guard did not catch them. The conservative finite-owner/support policy
returns 138.285446 px here for Sharp and MTSDF. This reflects intentional finite
distance at an above-cap acute tip, not restoration of an unlimited ideal miter.
Full-domain, between-ray and RGB bilinear checks accompany this diagnostic point.
Never alter the source to match a rendered result.
