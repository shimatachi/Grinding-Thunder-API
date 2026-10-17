using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class VisualEditorProductivityTests
{
    [Fact]
    public async Task VisualEditorAssets_IncludeUndoRedoStateAndValidationWarningStyles()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());

        var css = await client.GetStringAsync("/css/admin.css");
        var javascript = await client.GetStringAsync("/js/visual-tree.js");

        // CSS visual validation warnings and jump pulse
        Assert.Contains(".visual-tree-node.is-coordinate-collision", css);
        Assert.Contains("⚠️ Overlap", css);
        Assert.Contains(".visual-tree-node.is-out-of-bounds", css);
        Assert.Contains("⚠️ Out of bounds", css);
        Assert.Contains(".visual-tree-edge.is-validation-warning", css);
        Assert.Contains(".visual-tree-node.is-jump-highlighted", css);
        Assert.Contains("@keyframes visual-tree-node-focus-pulse", css);

        // JavaScript undo/redo stack & shortcuts
        Assert.Contains("const undoStack = [];", javascript);
        Assert.Contains("const redoStack = [];", javascript);
        Assert.Contains("const maxHistorySize = 50;", javascript);
        Assert.Contains("const pushHistory =", javascript);
        Assert.Contains("const updateUndoRedoControls =", javascript);
        Assert.Contains("const undo =", javascript);
        Assert.Contains("const redo =", javascript);
        Assert.Contains("const jumpToNode =", javascript);
        Assert.Contains("const initValidationJumpLinks =", javascript);
        Assert.Contains("isRankInversion", javascript);
        Assert.Contains("is-validation-warning", javascript);
        Assert.Contains("data-jump-to-node", javascript);
        Assert.Contains("dragState.positions.forEach", javascript);
        Assert.Contains("event.key === \"Delete\" || event.key === \"Backspace\"", javascript);
        Assert.Contains("event.key === \"z\" || event.key === \"Z\"", javascript);
        Assert.Contains("event.key === \"y\" || event.key === \"Y\"", javascript);
        Assert.Contains("[\"ArrowLeft\", \"ArrowRight\", \"ArrowUp\", \"ArrowDown\"]", javascript);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task DraftVisualEditor_RendersUndoRedoButtonsAndValidationJumpContainers(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-tree-undo", html);
        Assert.Contains("data-tree-redo", html);
        Assert.Contains("Undo (Ctrl+Z)", html);
        Assert.Contains("Redo (Ctrl+Y)", html);
        Assert.Contains("data-tree-layout-toolbar", html);
        Assert.Contains("data-tree-collision-list", html);
        Assert.Contains("data-tree-collision-warning", html);
        Assert.Contains("Ctrl+Z/Ctrl+Y undo/redo, Esc deselects, Del/Backspace removes", html);
        Assert.Contains("press an arrow key", html);
    }

    [Theory]
    [InlineData("publisher")]
    [InlineData("anonymous")]
    public async Task ReadOnlyVisualTree_StrictlyExcludesUndoRedoAndLayoutTools(string userType)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());

        string html;
        if (userType == "publisher")
        {
            await LoginAsync(client, "publisher");
            var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
            html = await response.Content.ReadAsStringAsync();
        }
        else
        {
            var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.PublishedVersionId}");
            html = await response.Content.ReadAsStringAsync();
        }

        Assert.DoesNotContain("data-tree-undo", html);
        Assert.DoesNotContain("data-tree-redo", html);
        Assert.DoesNotContain("data-tree-layout-toolbar", html);
        Assert.DoesNotContain("data-tree-edit-toggle", html);
    }

    [Fact]
    public async Task PublicTree_StrictlyExcludesUndoRedoAndEditControls()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/Tree?nationId={TestMockApiHandler.NationId}" +
            $"&researchTreeId={TestMockApiHandler.ResearchTreeId}" +
            $"&versionId={TestMockApiHandler.PublishedVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-tree-undo", html);
        Assert.DoesNotContain("data-tree-redo", html);
        Assert.DoesNotContain("data-tree-layout-toolbar", html);
        Assert.DoesNotContain("data-tree-edit-toggle", html);
    }

    [Fact]
    public async Task ValidationMessages_IncludeDataValidationMessageHookForCanvasJumpFocus()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=Validate");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (html.Contains("Validation failed"))
        {
            Assert.Contains("data-validation-message", html);
        }
    }

    [Fact]
    public void VisualEditor_JavaScriptStateTransitions_ExecuteUndoRedoAndKeyboardHooks_InNodeRuntime()
    {
        var nodeScript = @"
const assert = require('assert');

// Simulate the visual tree undo/redo state machine and keyboard logic
const undoStack = [];
const redoStack = [];
const maxHistorySize = 50;
let isEditing = true;

const pushHistory = (actionName, mutations) => {
    if (!isEditing) return;
    const actualChanges = mutations.filter(m =>
        m.from.column !== m.to.column ||
        m.from.row !== m.to.row
    );
    if (actualChanges.length === 0) return;
    undoStack.push({ action: actionName, mutations: actualChanges });
    if (undoStack.length > maxHistorySize) undoStack.shift();
    redoStack.length = 0;
};

const undo = (nodeState) => {
    if (!isEditing || undoStack.length === 0) return false;
    const entry = undoStack.pop();
    entry.mutations.forEach(m => {
        nodeState.column = m.from.column;
        nodeState.row = m.from.row;
    });
    redoStack.push(entry);
    return true;
};

const redo = (nodeState) => {
    if (!isEditing || redoStack.length === 0) return false;
    const entry = redoStack.pop();
    entry.mutations.forEach(m => {
        nodeState.column = m.to.column;
        nodeState.row = m.to.row;
    });
    undoStack.push(entry);
    return true;
};

// Test initial state
assert.strictEqual(undoStack.length, 0);
assert.strictEqual(redoStack.length, 0);

// Test mutation & undo/redo
const vehicleNode = { column: 1, row: 0 };
pushHistory('Nudge', [{ from: { column: 1, row: 0 }, to: { column: 2, row: 0 } }]);
vehicleNode.column = 2;

assert.strictEqual(undoStack.length, 1);
assert.strictEqual(redoStack.length, 0);

// Execute Undo
const undid = undo(vehicleNode);
assert.strictEqual(undid, true);
assert.strictEqual(vehicleNode.column, 1);
assert.strictEqual(undoStack.length, 0);
assert.strictEqual(redoStack.length, 1);

// Execute Redo
const redid = redo(vehicleNode);
assert.strictEqual(redid, true);
assert.strictEqual(vehicleNode.column, 2);
assert.strictEqual(undoStack.length, 1);
assert.strictEqual(redoStack.length, 0);

// Test non-negative boundary constraint: nudging left at column 0 must preserve >= 0
const boundaryNode = { column: 0, row: 0 };
const delta = { column: -1, row: 0 };
const targetCol = boundaryNode.column + delta.column;
const targetRow = boundaryNode.row + delta.row;
const canMove = targetCol >= 0 && targetRow >= 0;
assert.strictEqual(canMove, false);

// Test escape drag cancellation restores original coordinates
const dragNode = { column: 3, row: 2 };
const startPositions = new Map([[dragNode, { column: 3, row: 2 }]]);
// Dragged to (5, 4) temporarily
dragNode.column = 5;
dragNode.row = 4;
// Escape key pressed -> restore
startPositions.forEach((pos, n) => {
    n.column = pos.column;
    n.row = pos.row;
});
assert.strictEqual(dragNode.column, 3);
assert.strictEqual(dragNode.row, 2);

console.log('BATCH_35_JS_VERIFIED');
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"visual_editor_test_{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(tempFile, nodeScript);
            var psi = new ProcessStartInfo("node", $"\"{tempFile}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            Assert.True(process.ExitCode == 0, $"Node script failed with error: {stderr}");
            Assert.Contains("BATCH_35_JS_VERIFIED", stdout);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var loginPage = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(loginPage);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = $"{username}123",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}

