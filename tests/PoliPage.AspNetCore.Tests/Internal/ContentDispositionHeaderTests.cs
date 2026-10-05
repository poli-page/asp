using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.Tests.Internal;

public class ContentDispositionHeaderTests
{
    [Fact]
    public void ASCII_filename_produces_dual_form()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: false)
            .Should().Be("attachment; filename=invoice.pdf; filename*=UTF-8''invoice.pdf");
    }

    [Fact]
    public void Inline_flag_swaps_attachment_for_inline()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: true)
            .Should().Be("inline; filename=invoice.pdf; filename*=UTF-8''invoice.pdf");
    }

    [Fact]
    public void Non_ASCII_filename_produces_dual_form()
    {
        // ASP.NET Core's sanitizer replaces each non-ASCII UTF-16 code unit with '_' in the
        // ASCII fallback; the middle 't' is ASCII and is preserved.
        ContentDispositionHeader.Build("facture-été-2026.pdf", inline: false)
            .Should().Be("attachment; filename=facture-_t_-2026.pdf; filename*=UTF-8''facture-%C3%A9t%C3%A9-2026.pdf");
    }

    // Same cases as poli-page/django#1: control characters stripped, `\` and `"` escaped as
    // quoted-pairs, dual notation for non-ASCII names.
    [Theory]
    [InlineData("double-quote-is-escaped", "say \"hi\".pdf",
        "attachment; filename=\"say \\\"hi\\\".pdf\"; filename*=UTF-8''say%20%22hi%22.pdf")]
    [InlineData("backslash-is-escaped", "a\\b.pdf",
        "attachment; filename=\"a\\\\b.pdf\"; filename*=UTF-8''a%5Cb.pdf")]
    [InlineData("crlf-is-stripped", "evil.pdf\r\nSet-Cookie: sid=1",
        "attachment; filename=\"evil.pdfSet-Cookie: sid=1\"; filename*=UTF-8''evil.pdfSet-Cookie%3A%20sid%3D1")]
    [InlineData("control-chars-are-stripped", "tab\there\0\u001f\u007f.pdf",
        "attachment; filename=tabhere.pdf; filename*=UTF-8''tabhere.pdf")]
    [InlineData("parameter-injection-stays-inside-the-quoted-string", "x.pdf\"; filename=\"pwn.exe",
        "attachment; filename=\"x.pdf\\\"; filename=\\\"pwn.exe\"; filename*=UTF-8''x.pdf%22%3B%20filename%3D%22pwn.exe")]
    [InlineData("non-ascii-uses-rfc5987-dual-notation", "résumé François.pdf",
        "attachment; filename=\"r_sum_ Fran_ois.pdf\"; filename*=UTF-8''r%C3%A9sum%C3%A9%20Fran%C3%A7ois.pdf")]
    [InlineData("non-ascii-fallback-is-escaped", "résumé \"final\"\\v2.pdf",
        "attachment; filename=\"r_sum_ \\\"final\\\"\\\\v2.pdf\"; filename*=UTF-8''r%C3%A9sum%C3%A9%20%22final%22%5Cv2.pdf")]
    [InlineData("non-ascii-control-chars-are-stripped-from-both-forms", "résumé\r\n\u0085.pdf",
        "attachment; filename=r_sum_.pdf; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf")]
    public void Content_disposition_is_rfc6266_safe(string id, string filename, string expected)
    {
        ContentDispositionHeader.Build(filename, inline: false).Should().Be(expected, id);
    }

    [Fact]
    public void Inline_disposition_is_escaped_and_stripped_too()
    {
        ContentDispositionHeader.Build("q\"\r\n.pdf", inline: true)
            .Should().Be("inline; filename=\"q\\\".pdf\"; filename*=UTF-8''q%22.pdf");
    }

    [Fact]
    public void Empty_filename_throws()
    {
        Action act = () => ContentDispositionHeader.Build(string.Empty, inline: false);
        act.Should().Throw<ArgumentException>().WithParameterName("filename");
    }

    [Fact]
    public void Filename_made_only_of_control_chars_throws()
    {
        Action act = () => ContentDispositionHeader.Build("\r\n\t", inline: false);
        act.Should().Throw<ArgumentException>().WithParameterName("filename");
    }
}
