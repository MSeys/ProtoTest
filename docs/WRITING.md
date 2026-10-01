# Writing the ProtoTest docs

How the reference (`docs/docs`) and the Learn track (`docs/learn`) are written. Anyone who edits a page - a person
or an agent - follows this file. The goal is a technical site that is easy to read: precise, but never dense.

## Who reads it

A .NET developer who writes tests and knows ASP.NET Core and one test runner. They may never have written an
integration test that spans an API, a database and a browser, and they have not met ProtoTest's words yet. They
read in a hurry, often from a failing build, and come back later for depth.

## The reference and the track

| | Reference (`/docs`) | Learn (`/learn`) |
| --- | --- | --- |
| Answers | What does this do, and how do I use it? | How do I get good at this? |
| Reader | Has a task, scans for it | Follows lessons in order |
| Shape | Task first, then the details, then the limits | One skill per lesson: the problem, doing it, what you saw |
| Length | As long as the topic needs, details folded or moved to a child page | 5 to 10 minutes of reading |
| Tone | Direct, complete | Direct, patient, concrete |

## Check every page against these questions

1. **What is this page for?** One job per page. Its first paragraph says what the reader can do after reading it,
   in words they already know.
2. **Is it too much?** If the reader needs only part of the page for their task, the rest goes lower, into a fold
   (`<details>`), or to a child page. A section that only a few readers need is not on the first screen.
3. **Is it too little?** Every concept a page introduces has an example the reader can copy, and says what they will
   see when it works: the output, the trace, the failure message.
4. **Is it right?** Every API name, option, default, count and message matches the code and the samples. When a
   claim cannot be checked against the repository, it is flagged, not guessed.
5. **Is it clear?** Read the first sentence of each paragraph alone. If they do not tell the story, rewrite them.

## Sentences

- One idea per sentence. Most sentences stay under 25 words; none goes past 35.
- Say what happens, with a subject that acts: "The host starts the application", not "The application is started".
- Plain words first. Use a ProtoTest term only after it is introduced, and link its first use on the page.
- No chains of semicolons, no stacked parenthetical asides, no em-dashes. Split the sentence instead.
- No "simply", "just", "easily", "obviously" or "of course". If it were simple, the reader would not be here.
- Numbers from a run (milliseconds, counts) only when the point depends on them.
- Lists for steps and options; prose for reasons. A list item that needs three sentences is a paragraph.
- Headings say what the section does or answers: "Sign in as a test user", not "Authentication".

## Structure of a reference page

1. Title and one paragraph: what it is for, when to use it.
2. The smallest working example, with what it produces.
3. The common tasks, each a short section with code.
4. Details and options: tables at the end, or a child page.
5. Limits: what it does not do, said plainly.
6. Related pages: two or three links, not ten.

## Structure of a lesson

1. **The problem**, in two or three sentences of plain language. Something the reader has run into.
2. **What you will do**: two or three outcomes.
3. **Do it**: numbered steps, real code, and what to look for after each step.
4. **What happened**: the concept, explained after the reader has seen it, not before.
5. **Check yourself**: one question with the answer folded.
6. **Remember**: two or three lines. Then the next lesson.

A lesson teaches one new idea. Words from the trace (operation, phase, gap) are taught in a lesson before another
lesson uses them. A lesson still gives each ProtoTest word a few plain words at its first use on that page, even
when an earlier lesson taught it: readers skip around. The [Vocabulary](/docs/foundation/vocabulary) page lists them all, and a
new word gets a row there.

A lesson aims for 750 reading words or fewer: prose, callouts and checkpoint answers, not code. `prose-check`
counts them and flags a lesson over that line. The number is a warning, not a cap: cut repetition and reference
detail, never a step or the why a beginner needs. A lesson may go over when its report says what the extra words
teach. A lesson says what is true for the steps the reader does. Exceptions, edge
cases and limits belong on the reference page under **Go deeper**. A correction replaces the wrong sentence; it
does not add a caveat beside it.

A lesson is Markdown with this skeleton. Keep the headings; the page outline is built from them.

```mdx
---
id: the-file-name
title: Short task title
sidebar_position: 2
description: "One sentence: what the reader does in this lesson."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Short task title

<Lesson
  track="Reliable tests"
  step="Lesson 2 of 3"
  minutes={7}
  outcomes={['First thing you can do', 'Second thing you can do']}
  needs={['The previous lesson, or a tool to install']}
/>

## The problem

Two or three plain sentences about something the reader has run into.

## Do it

### 1. A step in imperative form

Code, then what the reader should see.

## What happened

The idea, explained now that the reader has seen it.

## Check yourself

<Checkpoint question="One question about this lesson's idea.">

The answer, in Markdown.

</Checkpoint>

## Remember

- Two or three lines worth keeping.

## Go deeper

- [A reference page](/docs/...): what it adds.
```

## Words

Use these terms, and only these, for these things. Introduce each one in plain words at its first use on a page.

| Term | Means | Not |
| --- | --- | --- |
| test run, run | One `dotnet test` invocation and everything it does | session, execution |
| host | The one object per test process that builds and owns what the run shares (`ProtoHost`) | container, server |
| setup class | The class that configures the host for a test project | bootstrap, fixture |
| test context, context | The per-test object behind `Proto.Context`: clients, state, files | scope, session |
| integration | A ProtoTest package for one kind of system: REST, SQL, Web | plugin, extension (in prose) |
| capability | What an integration lets a test do, once it is added to the host | feature |
| client | The object a test uses to talk to a system: `Proto.Context.Rest()` | proxy |
| attribute | A C# attribute that prepares something for a test and cleans it up | decorator |
| hook | Code that runs around every test or the whole run | interceptor |
| trace | The `.prototrace` archive a run writes | log, recording (as a noun) |
| operation | One recorded step in a trace: a call, a check, a setup step | span (except in OpenTelemetry pages) |
| check | An assertion as the trace records it | verification |
| phase | Setup, execution or teardown | stage |
| finding | Something a test reports without failing | warning (as a noun) |
| viewer | The ProtoTrace viewer at trace.prototest.dev | UI, dashboard |

## What an edit never changes

- Code blocks, API names, option keys, package names and commands, except to fix a proven error.
- Numbers and messages copied from a real run.
- What a sentence claims. A rewrite says the same thing more clearly. If the original is wrong or unclear in
  meaning, flag it instead of guessing.
- Links, except a broken one.
- Front matter `id`, slugs and file names, so URLs keep working.

## When an agent edits a page

Edit the page in place, then report per page:

```
## <path>
Changed: <one line per kind of change>
Moved or folded: <what, and where to>
Flagged: <claims to check, things missing, things that seem wrong>
```

Run `./proto docs check` and `./proto docs prose --base <ref> <files>` before
reporting. `--base` shows each page's reading words before and after the edit; a page that grows needs a reason.
