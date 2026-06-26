using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace SomeViewer.Services;

// A single, geometrically sorted DICOM series (one 3D image stack).
public sealed class DicomSeries
{
    public required string SeriesInstanceUid { get; init; }
    public required IReadOnlyList<DicomDataset> Slices { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int Depth => Slices.Count;
    public double RescaleSlope { get; init; } = 1.0;
    public double RescaleIntercept { get; init; }

    // Physical voxel spacing in millimeters, used to render the volume with the
    // correct proportions (CT slice spacing usually differs from in-plane spacing).
    public double PixelSpacingX { get; init; } = 1.0; // between columns (X)
    public double PixelSpacingY { get; init; } = 1.0; // between rows (Y)
    public double SliceSpacing { get; init; } = 1.0;  // between slices (Z)
}

// Loads a TCIA/NBIA "manifest-*" folder. The folder is a nested
// Collection/Patient/Study/Series tree, so instances are discovered recursively,
// grouped by Series Instance UID, and sorted along the slice axis.
public static class DicomFolderLoader
{
    public static IReadOnlyList<DicomSeries> LoadFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"DICOM folder not found: {folderPath}");
        }

        // 1. Recursively read every real DICOM instance (ignore extensions, csv, licenses, ...).
        var datasets = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .AsParallel()
            .Where(DicomFile.HasValidHeader)
            .Select(path => DicomFile.Open(path, FileReadOption.ReadLargeOnDemand).Dataset)
            .ToList();

        // 2. Group instances into series, then 3. sort each series along its slice axis.
        return datasets
            .Where(ds => ds.Contains(DicomTag.SeriesInstanceUID))
            .GroupBy(ds => ds.GetSingleValue<string>(DicomTag.SeriesInstanceUID))
            .Select(group =>
            {
                var first = group.First();
                var sortedSlices = group
                    .OrderBy(ComputeSliceLocation)
                    .ThenBy(ds => ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0))
                    .ToList();

                // PixelSpacing is [rowSpacing (Y), columnSpacing (X)] in mm.
                double spacingY = 1.0;
                double spacingX = 1.0;
                if (first.TryGetValues(DicomTag.PixelSpacing, out double[] ps) && ps.Length == 2)
                {
                    spacingY = ps[0];
                    spacingX = ps[1];
                }

                return new DicomSeries
                {
                    SeriesInstanceUid = group.Key,
                    Columns = first.GetSingleValueOrDefault<ushort>(DicomTag.Columns, 0),
                    Rows = first.GetSingleValueOrDefault<ushort>(DicomTag.Rows, 0),
                    RescaleSlope = first.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0),
                    RescaleIntercept = first.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0),
                    PixelSpacingX = spacingX,
                    PixelSpacingY = spacingY,
                    SliceSpacing = ComputeSliceSpacing(sortedSlices),
                    Slices = sortedSlices,
                };
            })
            .ToList();
    }

    // Copies the whole series into one contiguous 16-bit buffer (z-major),
    // decompressing encapsulated transfer syntaxes on the way.
    // Requires the fo-dicom.Codecs package for JPEG/JPEG2000 data.
    public static short[] LoadVolumeInt16(DicomSeries series)
    {
        int sliceLength = series.Columns * series.Rows;
        var volume = new short[(long)sliceLength * series.Depth];

        for (int z = 0; z < series.Depth; z++)
        {
            var dataset = EnsureUncompressed(series.Slices[z]);
            byte[] frame = DicomPixelData.Create(dataset).GetFrame(0).Data;

            // 16-bit little-endian pixels map directly onto short[].
            Buffer.BlockCopy(frame, 0, volume, z * sliceLength * sizeof(short), frame.Length);
        }

        return volume;
    }

    private static double ComputeSliceLocation(DicomDataset ds)
    {
        if (ds.TryGetValues(DicomTag.ImageOrientationPatient, out double[] iop) && iop.Length == 6 &&
            ds.TryGetValues(DicomTag.ImagePositionPatient, out double[] ipp) && ipp.Length == 3)
        {
            // Slice normal = rowCosines x columnCosines; project the position onto it.
            double nx = iop[1] * iop[5] - iop[2] * iop[4];
            double ny = iop[2] * iop[3] - iop[0] * iop[5];
            double nz = iop[0] * iop[4] - iop[1] * iop[3];
            return ipp[0] * nx + ipp[1] * ny + ipp[2] * nz;
        }

        return ds.TryGetSingleValue(DicomTag.SliceLocation, out double loc)
            ? loc
            : ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
    }

    // Z spacing in mm. Prefer the geometric distance between the first two sorted
    // slice positions (most reliable), then fall back to the standard tags.
    private static double ComputeSliceSpacing(IReadOnlyList<DicomDataset> sortedSlices)
    {
        if (sortedSlices.Count >= 2)
        {
            double gap = Math.Abs(ComputeSliceLocation(sortedSlices[1]) - ComputeSliceLocation(sortedSlices[0]));
            if (gap > 1e-4)
            {
                return gap;
            }
        }

        var first = sortedSlices[0];
        if (first.TryGetSingleValue(DicomTag.SpacingBetweenSlices, out double sbs) && sbs > 1e-4)
        {
            return sbs;
        }

        if (first.TryGetSingleValue(DicomTag.SliceThickness, out double thickness) && thickness > 1e-4)
        {
            return thickness;
        }

        return 1.0;
    }

    private static DicomDataset EnsureUncompressed(DicomDataset dataset)
    {
        if (!dataset.InternalTransferSyntax.IsEncapsulated)
        {
            return dataset;
        }

        var transcoder = new DicomTranscoder(
            dataset.InternalTransferSyntax,
            DicomTransferSyntax.ExplicitVRLittleEndian);
        return transcoder.Transcode(dataset);
    }
}
