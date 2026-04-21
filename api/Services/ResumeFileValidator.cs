using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;

namespace CareerCoach.Services;

/// <summary>
/// Validates uploaded resume files and normalizes metadata so PDFs/DOCX
/// files are accepted even when browsers strip the file extension.
/// </summary>
public static class ResumeFileValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".doc"
    };

    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["application/x-pdf"] = ".pdf",
        ["application/acrobat"] = ".pdf",
        ["applications/pdf"] = ".pdf",
        ["text/pdf"] = ".pdf",
        ["text/x-pdf"] = ".pdf",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/msword"] = ".doc",
        ["application/vnd.ms-word"] = ".doc",
        ["application/x-msword"] = ".doc"
    };

    public static bool TryNormalizeFileMetadata(
        IFormFile file,
        Stream bufferedStream,
        out string normalizedFileName,
        out string? errorMessage)
    {
        normalizedFileName = string.Empty;
        errorMessage = null;

        var extension = DetermineExtension(file, bufferedStream);
        if (extension == null)
        {
            var reported = GetReportedType(file);
            errorMessage = $"Unsupported file type ({reported}). Please upload a PDF, DOCX, or DOC resume.";
            return false;
        }

        var rawFileName = Path.GetFileName(string.IsNullOrWhiteSpace(file.FileName) ? "resume" : file.FileName.Trim());
        normalizedFileName = Path.ChangeExtension(string.IsNullOrEmpty(rawFileName) ? "resume" : rawFileName, extension);
        return true;
    }

    private static string GetReportedType(IFormFile file)
    {
        var ext = NormalizeExtension(Path.GetExtension(file.FileName));
        if (!string.IsNullOrWhiteSpace(ext))
        {
            return ext;
        }

        var mime = NormalizeMime(file.ContentType);
        return !string.IsNullOrWhiteSpace(mime) ? mime : "unknown format";
    }

    private static string? DetermineExtension(IFormFile file, Stream bufferedStream)
    {
        var fromName = NormalizeExtension(Path.GetExtension(file.FileName));
        if (IsAllowed(fromName))
        {
            return fromName;
        }

        var fromMime = NormalizeMime(file.ContentType);
        if (fromMime != null && MimeToExtension.TryGetValue(fromMime, out var mappedFromMime) && IsAllowed(mappedFromMime))
        {
            return mappedFromMime;
        }

        var fromSignature = DetectFromSignature(bufferedStream);
        if (IsAllowed(fromSignature))
        {
            return fromSignature;
        }

        return null;
    }

    private static string? DetectFromSignature(Stream bufferedStream)
    {
        if (!bufferedStream.CanSeek)
        {
            return null;
        }

        var originalPosition = bufferedStream.Position;
        bufferedStream.Position = 0;

        Span<byte> header = stackalloc byte[4];
        var read = bufferedStream.Read(header);

        bufferedStream.Position = originalPosition;

        if (read >= 4)
        {
            // PDF header starts with %PDF
            if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            {
                return ".pdf";
            }

            // DOCX files are zipped archives (PK..)
            if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            {
                return ".docx";
            }

            // Legacy .doc files are OLE compound documents.
            if (header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0)
            {
                return ".doc";
            }
        }

        return null;
    }

    private static string? NormalizeMime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var mime = contentType.Split(';')[0].Trim();
        return string.IsNullOrEmpty(mime) ? null : mime.ToLowerInvariant();
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static bool IsAllowed(string? extension)
    {
        return !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);
    }
}
