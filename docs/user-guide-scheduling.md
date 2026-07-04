> [User Guide](user-guide.md) > Scheduling

# User Guide: Scheduling

AI Agent Canvas includes a task scheduler powered by [Hangfire](https://www.hangfire.io/) that runs agent tasks in the background on a schedule or after a delay.

## What Scheduled Tasks Do

A scheduled task runs an agent prompt at a specified time or on a recurring schedule. The agent executes the prompt as if you had typed it in the chat, including access to all tools, personas, and context. Results are stored and can be retrieved later.

Use cases include:

- Periodic market data summaries
- Recurring report generation
- Timed alerts and checks
- Batch data processing

## Creating a Recurring Task

Ask the agent to create a recurring task using natural language:

```
Schedule a recurring task every hour to get stock quotes for AAPL and MSFT
and summarize the price movement
```

The agent calls `schedule_recurring_task` with a cron expression derived from your request. Common schedules:

| Request | Cron Expression |
|---------|----------------|
| Every hour | `0 * * * *` |
| Every day at 9 AM | `0 9 * * *` |
| Every Monday at 8 AM | `0 8 * * 1` |
| Every 30 minutes | `*/30 * * * *` |
| Every weekday at 6 PM | `0 18 * * 1-5` |

You can also specify a cron expression directly:

```
Schedule a recurring task with cron "0 9 * * 1-5" to generate a morning
market briefing
```

## Creating a One-Time Task

For tasks that should run once after a delay:

```
Schedule a one-time task in 30 minutes to check the current price of GOOGL
```

The agent calls `schedule_one_time_task` with the specified delay. The task runs once and stores its result.

## Viewing Scheduled Tasks

List all scheduled tasks:

```
List my scheduled tasks
```

The agent calls `list_scheduled_tasks` and returns the name, schedule, and status of each task.

## Getting Task Results

After a task has run, retrieve its results:

```
Get the results of my scheduled tasks
```

The agent calls `get_task_results` and returns the output from completed task executions.

## Removing a Scheduled Task

```
Remove the market briefing scheduled task
```

The agent calls `remove_scheduled_task` to cancel and delete the task.

## How It Works

The scheduling system uses three components:

1. **SchedulerToolProvider** -- Exposes the five scheduling tools to the agent: `schedule_recurring_task`, `schedule_one_time_task`, `list_scheduled_tasks`, `remove_scheduled_task`, and `get_task_results`.
2. **Hangfire** -- An open-source background job framework for .NET. It manages job queues, scheduling, retries, and persistence.
3. **SQLite storage** -- Task state is persisted in `hangfire.db`, so scheduled tasks survive backend restarts.

When a scheduled task fires, Hangfire invokes the agent with the stored prompt. The agent processes it using the same pipeline as a chat message, including tool calls and context injection.

## The Hangfire Dashboard

Hangfire provides a built-in web dashboard for monitoring and managing background jobs. Access it at:

```
http://localhost:5000/hangfire
```

The dashboard shows:

- **Recurring jobs** -- All recurring tasks with their cron expressions and next run times.
- **Succeeded jobs** -- Completed task executions with timestamps.
- **Failed jobs** -- Tasks that encountered errors, with exception details.
- **Processing jobs** -- Currently running tasks.
- **Scheduled jobs** -- One-time tasks waiting to execute.

Use the dashboard to inspect task history, trigger manual reruns, or delete jobs directly.

**Warning:** The Hangfire dashboard is exposed without authentication by default. In production, configure authentication middleware to restrict access. See the [Hangfire documentation](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html#configuring-authorization) for details.

## Autonomous Execution Mode

Beyond scheduled tasks, AI Agent Canvas supports fully autonomous execution. In this mode, the agent works independently on goals without waiting for user input.

### How It Works

1. **Create goals** -- Define what the agent should achieve using goal management tools or seed goals from custom agents.
2. **Enable autonomous mode** -- Ask the agent: *"Start autonomous mode"*. This registers a Hangfire recurring job.
3. **The agent works autonomously** -- The `AutonomousAgentJob` polls the work queue, claims items, executes them via `AIAgent.RunAsync`, and saves results. When the queue is empty, it picks the next active goal and creates work items for it.
4. **Monitor or stop** -- Ask *"Get autonomous status"* to check, or *"Stop autonomous mode"* to disable it.

### Goals and Work Queue

Goals are markdown-persisted definitions that describe what the agent should accomplish. Each goal has a name, description, priority (critical/high/medium/low), acceptance criteria, and an optional assigned agent.

```
Create a goal called "Daily Market Report" with priority high and description
"Generate a comprehensive market analysis report for the top 10 tech stocks every morning"
```

The work queue is a SQLite-backed transient queue (`workqueue.db`) where individual work items are submitted, claimed, and completed. The autonomous agent job processes items in priority order.

```
Submit a work item: "Get stock quotes for AAPL, MSFT, and GOOGL and summarize trends"
List the work queue
Get queue stats
```

### Goal and Work Queue Tools

| Tool | Description |
|------|-------------|
| `create_goal` | Create a new goal with priority and acceptance criteria |
| `list_goals` | List all goals, optionally filtered by status |
| `read_goal` | Read the full details of a goal |
| `update_goal_status` | Update a goal's status (active/completed/paused/cancelled) |
| `delete_goal` | Delete a goal |
| `submit_work_item` | Submit a work item to the queue |
| `list_work_queue` | List items in the work queue |
| `cancel_work_item` | Cancel a pending work item |
| `get_queue_stats` | Get work queue statistics |

### Autonomous Mode Tools

| Tool | Description |
|------|-------------|
| `start_autonomous_mode` | Enable autonomous execution and register the Hangfire recurring job |
| `stop_autonomous_mode` | Disable autonomous execution and remove the recurring job |
| `get_autonomous_status` | Check whether autonomous mode is currently enabled |

### Configuration

Autonomous execution is disabled by default. The options are:

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `false` | Whether autonomous mode is active |
| `MaxIterationsPerRun` | `5` | Maximum work items to process per job run |
| `PollIntervalSeconds` | `30` | Seconds between polls when queue is empty |
| `CronExpression` | `*/30 * * * * *` | Hangfire cron schedule for the recurring job |

## Tips

- **Tasks run server-side** -- They execute even when no browser is open.
- **Results are stored** -- You can retrieve results at any time after execution.
- **Tasks use the current agent configuration** -- The active persona, guardrails, and context at the time of execution apply to the task.
- **Monitor via notifications** -- Completed tasks can push results to the notification channel (`/api/notifications`), which the frontend receives via SSE.
- **SQLite persistence** -- The `hangfire.db` file stores all job state. Back it up if your scheduled tasks are important.

---
