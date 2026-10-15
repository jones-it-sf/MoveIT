using MoveIT.Core.Util;

namespace MoveIT.Core.Tests;

public class CsvReaderTests
{
    [Fact]
    public void ParsesSimpleRows()
    {
        var rows = CsvReader.Parse("a,b,c\n1,2,3\r\n4,5,6");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["4", "5", "6"], rows[2]);
    }

    [Fact]
    public void HandlesQuotedFields()
    {
        var text = "name,note\n"
            + "\"Smith, John\",\"He said \"\"hi\"\"\"\n"
            + "\"multi\nline\",ok\n";

        var rows = CsvReader.Parse(text);

        Assert.Equal("Smith, John", rows[1][0]);
        Assert.Equal("He said \"hi\"", rows[1][1]);
        Assert.Equal("multi\nline", rows[2][0]);
    }

    [Fact]
    public void SkipsBlankLinesAndBom()
    {
        var rows = CsvReader.Parse("\uFEFFa,b\n\n1,2\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b"], rows[0]);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void KeepsEmptyTrailingField()
    {
        var rows = CsvReader.Parse("a,b,c\n1,,3");

        Assert.Equal(["1", "", "3"], rows[1]);
    }

    [Fact]
    public void ReturnsNothingForEmptyInput()
    {
        Assert.Empty(CsvReader.Parse(""));
        Assert.Empty(CsvReader.Parse("\n\n"));
    }
}
