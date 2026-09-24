---
sidebar_position: 1
title: Why I built ProtoTest
description: The problems and earlier experience that led to ProtoTest.
---

# Why I built ProtoTest

I love programming, but especially building tools and solving abstract problems. I can build features, but problem-solving is like a puzzle.

Integration testing gave me plenty of those problems.

## Tests stopped being about the test

Integration testing can get rough, especially for bigger applications such as SaaS applications. There is infrastructure to start, users and tenants to create, authentication to arrange, data to clean up and different services to talk to.

At some point more of the test is about that setup than the behavior it is meant to check.

My focus has always been clean and readable code. I wanted common application setup outside the test, while setup that matters to the scenario should remain visible.

## I had built a testing framework before

ProtoTest was not my first attempt at this.

I had already built a similar testing framework by hand. It started around API testing and grew as we looked at which other parts of an application could be covered.

I learned a lot from building and using it. I also knew which parts I wanted to change. Its integrations were more mixed together, its core was less reusable and some lifecycle decisions could be cleaner.

ProtoTest was built from scratch, but the vision did not start from scratch.

## Why one foundation?

There are already good libraries for HTTP, browsers, containers and most other things ProtoTest works with. I did not build ProtoTest to replace them.

The problem for me was that every integration felt like its own island. Setup, authentication, cleanup and diagnostics were handled differently or had to be connected by the test project.

ProtoTest gives those integrations the same host, test context and lifecycle. They can use setup that already happened and write their operations to the same trace.

The integrations are still opinionated wrappers. They represent how I want to write tests with the libraries underneath them. That will not be the best choice for everyone.

## Why tracing?

Moving common setup outside a test makes the scenario easier to read, but it can also hide what happened before the test method ran.

That becomes a problem when a test only fails in CI, only fails sometimes or depends on several pieces of infrastructure. A failed assertion is often only the final part of the story.

ProtoTrace exists to show the lifecycle around that failure. It records setup, operations, checks, cleanup and captured evidence from the integrations that took part.

Playwright tracing was a large inspiration, but I wanted the trace to cover more than browser actions.

## What I wanted to build

I wanted tests to focus on the scenario again. I wanted integrations to work together instead of every one of them solving lifecycle and diagnostics again. I wanted failures to leave enough information behind to investigate them afterwards.

ProtoTest is my attempt to solve those problems.

It is a new project and real use will show where I got things wrong. That is part of building it too.
