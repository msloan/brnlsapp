using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

[ApiController]
public class EventsController : ControllerBase
{
    private const string AgentsHashKey = "agents"; 
    private const string SseChannel = "sse:messages"; 
    private readonly IConnectionMultiplexer _mux;
    private readonly IDatabase _db; 

    public EventsController(IConnectionMultiplexer mux)
    {
        _mux = mux;
        _db = mux.GetDatabase(); 
    }

    [HttpGet("/events")]
    public async Task GetEvents(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var sub = _mux.GetSubscriber();
        var messageQueue = await sub.SubscribeAsync(RedisChannel.Literal(SseChannel));

        await WriteSseCommentAsync("connected", ct);

        messageQueue.OnMessage(async channelMessage =>
        {
            if (ct.IsCancellationRequested) return;
            await WriteSseEventAsync("agent_update", channelMessage.Message, ct);
        });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await WriteSseCommentAsync("ping", ct); 
                await Task.Delay(TimeSpan.FromSeconds(25), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await messageQueue.UnsubscribeAsync();
        }
    }

    [HttpGet("/agents")]
    public async Task<IActionResult> GetAllAgents()
    {
        var agentEntries = await _db.HashGetAllAsync(AgentsHashKey);
        var agents = agentEntries.Select(entry => new AgentStatusDto(entry.Name.ToString(), entry.Value.ToString()))
                                 .ToList();
        return Ok(agents);
    }

    [HttpPost("/agents")]
    public async Task<IActionResult> AddAgent([FromBody] AddAgentDto dto)
    {
        var agentId = string.IsNullOrEmpty(dto.AgentId) ? Guid.NewGuid().ToString() : dto.AgentId; 
        var initialState = "running"; 

        await _db.HashSetAsync(AgentsHashKey, agentId, initialState);

        var updatePayload = JsonSerializer.Serialize(new AgentStatusDto(agentId, initialState));
        await _mux.GetSubscriber().PublishAsync(RedisChannel.Literal(SseChannel), updatePayload);

        return Created($"/agents/{agentId}", new AgentStatusDto(agentId, initialState));
    }

    [HttpPost("/agents/{agentId}/state")]
    public async Task<IActionResult> UpdateAgentState(string agentId, [FromBody] UpdateStateDto dto)
    {
        var state = dto.State?.ToLowerInvariant();
        if (state != "running" && state != "paused")
        {
            return BadRequest("Invalid state. Must be 'running' or 'paused'.");
        }

        await _db.HashSetAsync(AgentsHashKey, agentId, state);

        var updatePayload = JsonSerializer.Serialize(new AgentStatusDto(agentId, state));
        await _mux.GetSubscriber().PublishAsync(RedisChannel.Literal(SseChannel), updatePayload);

        return Ok(new AgentStatusDto(agentId, state));
    }

    private async Task WriteSseEventAsync(string eventName, RedisValue payload, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            await Response.WriteAsync($"event: {eventName}\ndata: {payload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing SSE event: {ex.Message}");
        }
    }

    private async Task WriteSseCommentAsync(string comment, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            await Response.WriteAsync($": {comment}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing SSE comment: {ex.Message}");
        }
    }

    public record AgentStatusDto(string AgentId, string State);

    public record AddAgentDto(string? AgentId);

    public record UpdateStateDto(string State);
}
