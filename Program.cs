// See https://aka.ms/new-console-template for more information
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Models.Interfaces;
using Microsoft.OpenApi.Models.References;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.Writers;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

var mcpServerUrl = args[0];
if (string.IsNullOrEmpty(mcpServerUrl))
{
    Console.WriteLine("Please provide the MCP server URL as a command line argument.");
    return;
}

var client = await CreateRemoteMcpClient(mcpServerUrl);

var tools = await client.ListToolsAsync();

var doc = CreateOpenApiDocument(client,tools);

// Serialize the OpenAPI document to JSON
using (var fileStream = new FileStream($"{client.ServerInfo.Name}-openapi.json", FileMode.Create, FileAccess.Write, FileShare.None))
{
    var streamWriter = new StreamWriter(fileStream);
    var writer = new OpenApiJsonWriter(streamWriter);
    doc.SerializeAsV3(writer);
    streamWriter.Flush();
}

async Task<IMcpClient> CreateRemoteMcpClient(string mcpServerUrl)
{
    McpClientOptions options = new()
    {
        ClientInfo = new() { Name = "OpenApiFromMCP", Version = "1.0.0" }
    };

    var transport = new SseClientTransport(new SseClientTransportOptions()
    {
       Endpoint = new Uri(mcpServerUrl),
    });

    return await McpClientFactory.CreateAsync(transport, options);
}


OpenApiDocument CreateOpenApiDocument(IMcpClient client, IList<McpClientTool> tools)
{
    var toolCallSchemas = new List<IOpenApiSchema>();
    var schema = new OpenApiSchema
    {
        OneOf = toolCallSchemas
    };

    var openApiDoc = new OpenApiDocument
    {
        Info = new OpenApiInfo
        {
            Title = client.ServerInfo.Name,
            Version = client.ServerInfo.Version,
            Description = "OpenAPI for MCP Server"
        },
        Paths = new OpenApiPaths() {
        {"/", CreateMessagesPathItem(schema)}
        },
        Components = new OpenApiComponents()
        {
            Schemas = []
        }
    };

    foreach (var tool in tools)
    {
        string input = tool.JsonSchema.ToString();
        var inputSchema = OpenApiModelFactory.Parse<OpenApiSchema>(input, Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0, openApiDoc, out OpenApiDiagnostic diagnostic);
        var toolCallParametersName = $"{tool.Name}Parameters";
        openApiDoc.Components.Schemas.Add(toolCallParametersName, inputSchema);
        OpenApiSchema toolCallSchema = CreateToolCallSchema(tool.Name, new OpenApiSchemaReference(toolCallParametersName,openApiDoc));
        var toolCallSchemaName = $"{tool.Name}Request";
        openApiDoc.Components.Schemas.Add(toolCallSchemaName, toolCallSchema);
        toolCallSchemas.Add(new OpenApiSchemaReference(toolCallSchemaName,openApiDoc));
    }
    return openApiDoc;
}

static OpenApiSchema CreateToolCallSchema(string name, IOpenApiSchema? toolSchema)
{
    var toolCallSchema = new OpenApiSchema
    {
        Type = JsonSchemaType.Object,
        Properties = new Dictionary<string, IOpenApiSchema>
            {
                { "toolId", new OpenApiSchema { Type = JsonSchemaType.String, Enum = [name] } },
                { "params", toolSchema }
            },
        Required = ["toolId"]
    };
    return toolCallSchema;
}

static OpenApiPathItem CreateMessagesPathItem(OpenApiSchema schema)
{
    return new OpenApiPathItem()
    {
        Operations = new Dictionary<HttpMethod, OpenApiOperation>()
            {
                { HttpMethod.Get, new OpenApiOperation() {
                    Description = "Get the list of tools",
                    Responses = new OpenApiResponses()
                    {
                        { "200", new OpenApiResponse() { Description = "OK" } }
                    }
                }},
                { HttpMethod.Post, new OpenApiOperation() {
                    Description = "Execute a tool",
                    RequestBody = new OpenApiRequestBody()
                    {
                        Content = new Dictionary<string, OpenApiMediaType>()
                        {
                            { "application/json", new OpenApiMediaType() { Schema = schema } }
                        }
                    },
                    Responses = new OpenApiResponses()
                    {
                        { "200", new OpenApiResponse() { Description = "OK" } }
                    }
                }}
            }
    };
}