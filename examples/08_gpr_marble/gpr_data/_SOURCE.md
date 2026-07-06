# Botticino marble quarry GPR (IDS GeoRadar GRED HD) -> GprIdsDtReader

Date: 2026-05-27
Source: Bondua, Tinti et al. 2024, "A Set of Ground Penetrating Radar Measures from
        Quarries", MDPI Data 9(3):42; Mendeley Data DOI 10.17632/w26n6nftxs.3 (v3),
        site Italy-Botticino (marble). Extracted from
        original archive 'Ground Penetrating Radar measures from quarries.zip' -> RadargramData.zip.
Topic: real marble-quarry GPR for the Frahan GprIdsDtReader (.dt + .hdr_dt).
License: CC BY 4.0 (per data/ATTRIBUTION.md; upstream Mendeley DOI 10.17632/w26n6nftxs.3, MDPI Data 10.3390/data9030042). An earlier CC-BY-NC-ND label was a mislabel.
Status: raw
Do not edit: true

Files staged here (a small subset; the full set stays in the external zip):
- LA010001.DT + LA010001.HDR_DT, LA010002.DT + LA010002.HDR_DT (IDS GRED HD pairs).

Format (verified): record stride len_rec = 4 + 2*samples; 'V' magic; header records
then 'R' trace records (marker1 int16 + marker2 int16 + samples int16). Smoke-tested:
LA010001.DT -> 185 traces x 512 samples, dz 0.0078 m, dx 0.0260 m.
CC BY 4.0: free to use with attribution (the earlier non-commercial label was incorrect).
