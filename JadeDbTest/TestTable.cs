using JadeDbClient.Attributes;

namespace JadeDbTest;

[JadeDbTable("tbl_Test")]
[JadeDbObject]
public partial class TestTable
{
    [JadeDbColumn("id", IgnoreOnInsert = true, IsIdentity = true)]
    public int Id { get; set; }

    [JadeDbColumn("name")]
    public string UserName { get; set; } = "";
}