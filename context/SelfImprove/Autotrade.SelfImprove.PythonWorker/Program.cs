using System.Text.Json;
using PythonScript.Contexts;

var requestJson = (await Console.In.ReadToEndAsync().ConfigureAwait(false))
    .TrimStart('\uFEFF');
var request = JsonSerializer.Deserialize<PythonWorkerRequest>(
    requestJson,
    new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("Worker request is empty.");

if (string.IsNullOrWhiteSpace(request.StrategyModulePath)
    || !File.Exists(request.StrategyModulePath))
{
    throw new FileNotFoundException("Strategy module was not found.", request.StrategyModulePath);
}

var wrapperPath = WriteWrapper(request.StrategyModulePath);
try
{
    using var runtime = new PythonScriptRuntime(wrapperPath);
    var result = runtime.ExecuteFunction("evaluate_contract", request.RequestJson);
    if (result is not string responseJson || string.IsNullOrWhiteSpace(responseJson))
    {
        throw new InvalidOperationException("Python strategy returned an empty JSON response.");
    }

    Console.Out.Write(responseJson);
}
finally
{
    TryDelete(wrapperPath);
}

static string WriteWrapper(string strategyModulePath)
{
    var wrapperPath = Path.Combine(
        Path.GetTempPath(),
        $"autotrade_python_strategy_{Guid.NewGuid():N}.py");
    var modulePathLiteral = JsonSerializer.Serialize(Path.GetFullPath(strategyModulePath));

    File.WriteAllText(wrapperPath, $$"""
import importlib.util
import json

MODULE_PATH = {{modulePathLiteral}}

def evaluate_contract(request_json):
    spec = importlib.util.spec_from_file_location("generated_strategy", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("failed to load generated strategy module")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not hasattr(module, "evaluate"):
        raise RuntimeError("generated strategy module must expose evaluate(input)")
    response = module.evaluate(json.loads(request_json))
    return json.dumps(response, separators=(",", ":"), ensure_ascii=False)
""");

    return wrapperPath;
}

static void TryDelete(string path)
{
    try
    {
        File.Delete(path);
    }
    catch
    {
        // Worker temp cleanup must not mask strategy execution results.
    }
}

internal sealed record PythonWorkerRequest(
    string StrategyModulePath,
    string RequestJson);
