---
sidebar_position: 7
title: AI usage
description: "How and why AI was used while building ProtoTest, and how its output was checked before it shipped."
---

# AI usage

Yes, I used AI extensively while building ProtoTest. I used multiple models from different providers.

## Why did I use it?

I wanted to build ProtoTest. I had already built a similar testing framework from scratch before the AI boom. It covered less and its core was harder to reuse. Building and using it taught me what to change.

ProtoTest itself was built from scratch, but I already had the vision for most of its integrations. Some integrations are new. Others were split up or reworked because I wanted to do them better this time.

AI allowed me to work through ideas, alternatives and implementations much faster. It also helped me learn more about protocols I had less experience with.

## How did I use it?

I did not just ask a model to create a testing framework and release what it generated.

I used different models because I am critical of AI output and know what it can produce without enough direction. I compared suggestions, kept setting boundaries, said no often and changed direction when something did not fit.

I wanted the speed, but I did not want AI to decide what ProtoTest became.

AI helped write code and documentation. The problems ProtoTest tries to solve, the earlier experience behind it and the direction I kept pushing it towards came from me.

## What did it cost?

Speed has costs, and they showed up here too: generated text drifts toward one voice, a project can grow faster than its design, and anything wrong still needs understanding before it can be fixed. The answer to all three was the same: nothing ships as generated.

| What AI drafted | What changed before it shipped | Which gate proves it |
| --- | --- | --- |
| Code across the integrations | Compared suggestions from several models, set boundaries, said no often, changed direction when something did not fit | The test suite and the review bar (`./proto verify`) |
| Documentation and public explanation | Rewritten in my own words, in a second pass over every page | The docs checks and the review bar (`./proto docs check`, `./proto verify`) |
| Anything wrong | Understood and fixed by the maintainer | If generated code is wrong, that is still my problem to understand and fix |

## Do I regret using it?

I don't know yet.

It feels strange seeing how much it sped things up, but also how easy it is to lose your own voice or let a project grow too quickly. That is one reason I am going through the documentation and public explanation again in my own words.

I maintain ProtoTest and decide what is included. If generated code is wrong, that is still my problem to understand and fix.

ProtoTest is still what I wanted to build.

For the tools ProtoTest builds for coding agents, see [Agent workflows](../agent-workflows/coding-agents.md). This page is about how the project itself uses AI.
