using Xunit;

namespace JsonRepair.Tests.TestCases;

public class JsonEdgeCaseData : TheoryData<string, string, string>
{
    public JsonEdgeCaseData()
    {
        // (ID, Input, ExpectedOutput)
        Add("TC01", "{\"name\": \"John\", \"age\": 30}", "{\"name\": \"John\", \"age\": 30}");
        Add("TC02", "```json\n{\"a\": 1}\n```", "{\"a\": 1}");
        Add("TC03", "{'key': 'value'}", "{\"key\": \"value\"}");
        Add("TC04", "{foo: \"bar\", age: 25}", "{\"foo\": \"bar\", \"age\": 25}");
        Add("TC05", "{\"active\": True, \"data\": None, \"flag\": False}", "{\"active\": true, \"data\": null, \"flag\": false}");
        Add("TC06", "{\"val\": undefined, \"num\": NaN}", "{\"val\": null, \"num\": null}");
        Add("TC07", "{\"a\": 1, \"b\": 2,}", "{\"a\": 1, \"b\": 2}");
        Add("TC08", "[1, 2, 3,]", "[1, 2, 3]");
        Add("TC09", "{\"a\": 1 \"b\": 2}", "{\"a\": 1, \"b\": 2}");
        Add("TC10", "[1 2 3]", "[1, 2, 3]");
        Add("TC11", "[1, 2, 3", "[1, 2, 3]");
        Add("TC12", "{\"a\": 1, \"b\": {\"c\": 2", "{\"a\": 1, \"b\": {\"c\": 2}}");
        Add("TC14", "{\"a\": 1 // single line comment\n}", "{\"a\": 1}");
        Add("TC15", "{\"a\": /* multi line */ 1}", "{\"a\": 1}");
        Add("TC18", "Here is the repaired JSON: {\"a\": 1} Enjoy!", "{\"a\": 1}");
        Add("TC20", "{'path': 'C:\\\\Users\\\\test'}", "{\"path\": \"C:\\\\Users\\\\test\"}");
        Add("TC21", "{\"val\": 1.2e-3, \"big\": 1E+10}", "{\"val\": 1.2e-3, \"big\": 1E+10}");
        Add("TC22", "{\"text\": \"line1\bline2\f\"}", "{\"text\": \"line1\\bline2\\f\"}");
        Add("TC23", "{None: 1}", "{\"None\": 1}");
        Add("TC24", "{True: 'yes'}", "{\"True\": \"yes\"}");
        Add("TC25", "{a: None}", "{\"a\": null}");
        Add("TC26", "{\"a\": 1]", "{\"a\": 1}");
        Add("TC27", "[1, 2}", "[1, 2]");
        Add("TC28", "{]", "{}");
        Add("TC29", "{\"a\": [1, 2}", "{\"a\": [1, 2]}");
        Add("TC30", "[1 None]", "[1, null]");
        Add("TC31", "\"[\"", "\"[\"");
        Add("TC32", "\"{\"", "\"{\"");
        Add("TC33", "\"[1,2,3,]\"", "\"[1,2,3,]\"");
        Add("TC34", "Here is your JSON: {\"a\": 1}", "{\"a\": 1}");
    }
}
