using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.Tests.Internal;

public class ContentDispositionHeaderTests
{
    [Fact]
    public void ASCII_filename_produces_basic_form()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: false)
            .Should().Be("attachment; filename=\"invoice.pdf\"");
    }

    [Fact]
    public void Inline_flag_swaps_attachment_for_inline()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: true)
            .Should().Be("inline; filename=\"invoice.pdf\"");
    }

    [Fact]
    public void Embedded_quote_is_backslash_escaped()
    {
        ContentDispositionHeader.Build("invoice\"of-doom.pdf", inline: false)
            .Should().Be("attachment; filename=\"invoice\\\"of-doom.pdf\"");
    }

    [Fact]
    public void Non_ASCII_filename_produces_dual_form()
    {
        // Mirrors the algorithm in /Users/mickael/Projects/nextjs/src/headers.ts: replace each
        // non-printable-ASCII char (one C# char at a time, UTF-16 code units) with '_' in the
        // ASCII fallback. The plan's expected value ("facture-___-2026.pdf") had a typo — the
        // middle 't' is ASCII and is preserved, so the correct fallback is "facture-_t_-2026.pdf".
        // The percent-encoded form "facture-%C3%A9t%C3%A9-2026.pdf" already reflects this.
        var actual = ContentDispositionHeader.Build("facture-été-2026.pdf", inline: false);
        actual.Should().StartWith("attachment; filename=\"facture-_t_-2026.pdf\"");
        actual.Should().Contain("filename*=UTF-8''facture-%C3%A9t%C3%A9-2026.pdf");
    }

    [Fact]
    public void Empty_filename_throws()
    {
        Action act = () => ContentDispositionHeader.Build(string.Empty, inline: false);
        act.Should().Throw<ArgumentException>().WithParameterName("filename");
    }
}
