---
title: Example Project
summary: A small service that does one thing and keeps doing it.
date: 2025-11-02
tags: [C#, .NET, Docker]
repo: https://github.com/your-handle/example-project
featured: true
---

**This is example content.** Copy this file, rename it, and describe one of your own projects.

## What it does

Watches a directory, and when a file lands in it, does the boring thing that used to be done by
hand. Runs in a container on a small board and has needed no attention since it was deployed.

## Why it exists

The manual version took four minutes and was forgotten roughly one week in three.

## How it works

```csharp
// The whole loop, minus the error handling.
await foreach (var file in watcher.WatchAsync(cancellationToken))
{
    var result = await processor.ProcessAsync(file, cancellationToken);
    logger.LogInformation("Processed {File}: {Result}", file.Name, result);
}
```

## What I would change

The retry policy is a fixed delay. Exponential backoff with jitter would be one line and would
behave better when the downstream service is having a bad afternoon.
