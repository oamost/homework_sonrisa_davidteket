# Human prompts

## Prompt 1

hi. in this session, we will work on a project. the current working directory is empty. init an empty git repository in it (remote at https://github.com/oamost/homework_sonrisa_davidteket.git). we will work only in this repository. please also create an AGENTS.md in the repo root, where all the rules will take place, specialized for this project work. as I tell you the rules on what to do, what not to do, and so these rules must go into this md file. 

what to do:

1. commits must be done after a milestone has been reached, with tangible evidence and human approval. each commit message must also describe what have been done, what was verified. the commit message's title must be equal to the deliverable that the commit ships.  

2. prompt history must be recorded for this work. to be easily found, create it as prompts.md in the repo root, so anyone exploring the repo should find it easily. this must not include your reasoning, only my prompts with UTC timestamps.

3. the layout of the repo will be the following with the constraints described after each directory name:

./docs : every human oriented documentation that gets created during the work must be placed here. this includes planning, decision making, evidence, logs, screenshots. 
./out  : all produced working artifacts and deliverables will be deployed here. 
./src  : all source code will go here, including tests
./ctx  : all the agentic knowledge must be placed into this location. AGENTS.md must be updated in case a new md file gets created in it for  uptodate agentic retrieval.

4. you as a GPT-6 Sol medium effort model will be responsible for orchestrating sections of the work for subagents (DeepSeek, GLM, Qwen 64k - local model, only very small tasks). rule of thumb is that serena MCP must be used for efficient symbol retrieval, and superpowers skills must also be used for the given task. these must only receive small scoped, but precisely defined tasks they must deliver. you can paralellize independent work through them, e.g.: one could do research, while the other can write code.

5. if something is unsure, undefined, non-specified: don't hesitate to ask questions, provide numbered options. communicate clearly. we use only english during the work. 

6. the entire work will be tracked by two files:

./ctx/goals/plan.md : the fully fleshed out definition on what needs to be done without blind spots. 
./ctx/goals/tasks.md : derived from plan.md, a fine granulated document, that contains all the atomic tasks needed to proceed towards the fulfillment of plan.md, one-by-one. totally symmetric, no information loss, no inconsistency against plan.md. it supports us to make progress one-by-one. I will steer the agent on which task we will iterate on next.

what not to do:

1. don't make autonomous decisions blindly, as we will proceed with the given task, if something unexpected pops up (such as design direction, decision making on behavior), I as a human want to be part of the decision loop

2. don't overthink and don't get into rabbitholes, such as going after a failing api request. I'm here with you along the work. if you encounter something like this, stop, and we will take a look at it.  

3. even if something is considered done according to the deliverables, it is not done until I as a human verified the results.  

4. gaslight me into false tracks (always be honest and professional)

5. don't read whole files in the repo at once. let the subagents do the heavy lifting. 


further instructions will follow after the initial steps have been made.

## Prompt 2

good. now, before we proceed with anything, please do a pre-flight check: if serena is available, if superpowers skills is available, if qwen (local) and deepseek (opencode) and openrouter available (free models only). just probe these models
to be certain if they are available.  

## Prompt 3

make it available

## Prompt 4

excellent. I noticed that plan.md got populated with the initial setup plan. erease these. plan.md will only contain the goals specified shortly. also on tasks.md you can keep the populated steps so far, but please make sure that the we use matrix format where every relevant column will be present, such as order, goal, description, status, timestamp

## Prompt 5

one another addition: we must tier the tasks.md into separate matrices, where one section contains a matrix for deliverable x (like the init prep work we are doing right now) and upcoming matrices for deliverable y and so on. this way I can clearly distinguish where are we located and things won't get bulky.

## Prompt 6

phrase these as rules into AGENTS.md (related to tasks.md and plan.md) so future agents won't forget info on these

## Prompt 7

one minor thing I would like to update the AGENTS.md's Agent Workflow section with: delegating small tasks to subagents is mandatory, not optional. although it might not make sense to do it every time. this is solely must be decided by GPT-6 Sol medium as the parent orchestrator agent. the delegated work must inform these subagents about the rules, the tools and the exact specific requirements, no whole file reads, only targeted changes may apply, thus research on a given topic must also be kept small scoped for these subagents.  

## Prompt 8

hmm, I see that timestamps are not accurate. it says that "(UTC; receipt time unavailable)". if there is an alternative way to resolve this, please do so. if not remove timestamping from prompts.md and tasks.md (update AGENTS.md accordingly) so we won't clutter the log with false timestamps.

## Prompt 9

also please update AGENTS.md with task.md related change (update the document too): status must be the last column with the following possible status symbols: "TODO, WIP, TESTING, DONE".  

## Prompt 10

all looks good. approving prep steps 01-15. next, I will provide the initial project goals.  

## Prompt 11

right. hereby I provide the initial project goals:

""We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too." "

before we even start to get into the planning, we must brainstorm on what this thing wants to be,
as there is no pre-existing solution or system to integrate this, all we have is this repository we are currently sitting on.

to summarize:

- no tech stack / language constraints, no existing system available
- whatever the goals require, we must ship it, this is a strategical angle
- some terms are not clear and might found undefined. me as a human and you as an agent are here to 
carve it out via brainstorming. 
- planning this will be crucial, as it decides the fate of the entire solution. once the 
planning (research) has been made, we will freeze plan.md, as we won't add any adhoc definitions later on.

if there is anything I missed, or if there is anything I must answer to you, please.
note that I have only the same information as you have from this prompt, I have no more info other than that 

## Prompt 12

skip such questions (no easy answers for them), let's get to have you start first 

## Prompt 13

let us review those risks first. interview me in forms of questions if you need decision or advice on them 

## Prompt 14

I think you found a grain of sand in the desert - don't get distracted by it: there are almost infinitely more sources to select from out there. 
I'm pretty sure we can figure the fitting providers. first things first:

1. yes, worldwide coverage, major and all disaster types across all regions. 
2. look up such feed providers on these categories. there are plenty of aggregators out there
3. all of the initially defined "important things" must be included, and potentially more as "important" is not a concrete matter
4. github might be a good place to look for such providers. we won't register into such services for api keys and so on.  

## Prompt 15

(1) event-data providers only. (2) I'm not sure of. is it possible to user such services without api keys? e.g. sending a slack notification witout us having a slack api key? 

## Prompt 16

sounds good and doable. for a moment, let us drive back to the first part of the original requirement, that is "We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. " - can we assess, how much we gathered for this?  

## Prompt 17

goal: approved
sources: focus only on gathering these sources and actually use them. this is an inhouse project for me, not for a large audience, plus it is not for monetization purposes. 
alert behavior: just think of a CNN feed or a reddit trending. these mediums always display relevant (hence "important") news in regards to these events
evidence to freeze the plan: coverage matrix only needed for the original requirement for now. we carve out the plan from it, then the tasks.md will act as a coverage matrix when the plan will be frooze

## Prompt 18

collect more into requirement-coverage.md so:

1. worldwide coverage could be reached
2. whichever service you probed refuses to talk, blacklist them. then find the next suitable provider candidate for the given source feed type

## Prompt 19

one thing I noticed in the meanwhile. the plan.md document's first section must contain the original requirement prompt verbatim (with quote symbols), as it acts as the root requirement of the soon-to-be defined detailed requirement.
I noticed that major hazard coverage is missing things like "war", "politics", and other types of "breaking news" candidates. need to fine tune on that. then print a summary matrix here on what is the current worldwide coverage per
"We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. "  

## Prompt 20

fine. now print a coverage matrix on what we have so far, and also expands on it as what is considered "important" news in the world (globally). from 
that we can identify the missing gaps and add them

## Prompt 21

a few things I would like to align:

1. "Cyber and critical infrastructure" : AI, breaches, acquisitions might be relevant here
2. "High-impact science and technology" : breakthroughs (multi-year), genuinely innovative inventions (like something was invented will having an effect on the world)

plus also include in a reprinted matrix after the alignments above have been done that:

1. plus one extra column with the aggregation sources listed
2. importance score (truncated (0,..,1)

## Prompt 22

this looks exciting, amazing. I think we are about to have all the info needed for "We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking
news, market movements, natural disasters, that kind of thing. ", right?  

## Prompt 23

I approve the coverage matrix, I think we killed it with all the important categories and source feeds. next, we should identify where all these we create will take place, in what form, where or how will it run, etc. 

## Prompt 24

account switch happened. verify that you know where we were at and what is the very next step must be fulfilled. 

## Prompt 25

let's focus on "where the system will run and in what form. it will help us planning the rest of the original requirement, which was "“We want users to be able to set up alerts so they get notified " and  "Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too.". again, we aren't developing a commercial product here, nor we have to serve on scale. you can consider the most lightweight, yet workign architecture these might require. I'm open to suggestions.  

## Prompt 26

I'm considering deploying it to github (for hosting) so if someone wants to try it out, they can without the hassle of building it and hosting it locally. what is your opinion on that?
to reflect to the rest of your proposal:

1. SQLite, I love it, proposal accepted
2. SMTP and slack approach previously clarified, no API key needed, approved.
3. the original requirement mentions "we need an admin view too", it means we will need some sort of frontend for this
4. for the code part, my proposal is c# as that's the one I have experience with

## Prompt 27

oh, I didn't know that. in that regards, I undo my github hosting proposal. I'm proposing another strategy. there should be a run.sh (or .ps) equivalent
in the repo root (do not implement them yet, we are purely in the planning phase), that would launch the entire deploy (build -> test -> run) pipeline locally (github actions turned off)
so the user could just launch the script and start using this application without any hastle. it should be containerized and should work idempotently on both windows and linux. 
in respect to all of these, how are we complying with the ""where the system will run and in what form. it will help us planning the rest of the original requirement, which was "“We want users to be able to set up alerts so they get notified " and  "Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too.". " requirements. aren't my proposals far fetched? my aim is something lightweight as well without the overkill 

## Prompt 28

each person running their own local copy. rest is agreed. 

## Prompt 29

this is misleading: "Provide root-level Linux and Windows launch scripts" this could be read as requiring root privileges. however, what I said was these scripts will be placed in the repo dir's root. fix these. 

## Prompt 30

thank you. now please print a coverage matrix per original requirement as where we are standing right now and what details left to be planned out 

## Prompt 31

okay, I will aid you through the rest of the uncut ties:

1. "Users set up alerts" : simple as that. once they subbed to an event type (see event type coverage matrix), they get notified on configured channels once something important happens 
2. "Breaking news worldwide" : I think we are having a great coverage on all types globally. breaking news must be inferred algorithmically and deterministically by taking various factors into account. use metadata to help these algorithms determine what is breaking news (important) and importance scoring. I cannot provide details beyond that.
3. "Market movements" same applies as for 2.
4. "Natural disasters worldwide" same applies as for 2. 
5. "“Something important” and other event kinds" already been discussed
6. "Users get notified" - timing: each 5 min after a subbed important news item appears. try not to bomb the user with a large amount of notifications at once. retries allowed, although should not be excessive and should be reasonable.
7. "More channels later" more notification channels should be supported out of the box without requiring production code change. e.g I would like to receive notifications on other platforms we have not pre-coded our application to
8. "Admin view" - it should include everything such application might need to put behind an admin wall
9. "Where and how it runs" - perfect so far. the frontend can be react or angular, I don't mind. 

to add these to the plan: 

1. plan tests as well - non-trivial methods will be unit tested, major functions will receive integration tests. 
2. feature inventory - we will measure what is really tested by either unit and integration tests. a simple agentic matrix
3. documentation - each implemented user facing feature must be documented in a user guide as we progress 
4. evidence - every task which contributes to implementing the original request (tasks.md as a whole) must also provide evidence along the way (logs, screenshots) 

## Prompt 32

a notification must be served under 5 minutes after an important event occurs in the world. yes, even if the feed reports it late. if the computer was off, skip those. we only care about those when the application is running.
are we ready to define the tasks making what we just defined so far? I think the picture starts assembling together 

## Prompt 33

make the application look up important news in the previous 24 hours so the user won't miss a thing even if the thing was offline. to answer your other question: send it as a normal notification. we can be a bit lazy with time here. the goal is to not blow the user up with large amount of notifications at once plus not leaving the user out of the loop in case the app was just started or if a feed published an important news item late 

## Prompt 34

" I’ll keep the catch-up scope tied to “important news” as you worded it, rather than silently extending it to market and disaster alerts." make no mistake on that. I didn't say that you must restrict the scope. this applies to all categories, market and disaster (and others defined) as well. it was just a way of phrasing my thought. 

## Prompt 35

so, are you ready to have tasks.md detailing tasks required for implementing the entire thing based on what we discussed so far?
I also approve the following from deliverable 2: 01-08. 
09 is not done yet as the plan is not ready to be frozen. 
10 is the upcoming task just asked you about above. 

## Prompt 36

one thing before the plan freeze: given those 30 tasks put together, infer if the original request will be satisfied? 

## Prompt 37

"The 30 tasks cover the components of the original request," that's all we need. don't worry about bulletproofing. we will address every single one of them as we process each task. 
now, I approve tasks.md's delverable 2 sections 09 and 10. thus you can freeze plan.md (mark it as feature freezed on top of the document), do the same for tasks.md (status updates only allowed from now).
then go and commit per rules. 

## Prompt 38

one follow up I forgot in regards of the plan.md - this document should contain 2 sections:

1. is the original request verbatim (it already contains it
2. the documented plan of all tasks combined in tasks.md (so the 2 documents can be symmetric)  (in human readable form)

get this done, then I'll take a look at it 

## Prompt 39

good job. I have spotted this "React or Angular are acceptable; neither is selected yet. " I select angular 

## Prompt 40

you can commit it now 

## Prompt 41

we can start implementing the application now. from now on, we will solely focus on deliverable 3 items. first, let's tackle the task responsible for laying the app's foundation 

## Prompt 42

is that part of the specification? 

## Prompt 43

only a subagent can investigate it via openrouter or qwen 

## Prompt 44

while qwen works, you could launch deepseek to investigate how to plug those sources into our application. put the associated task(s) into WIP status. 

## Prompt 45

whats up with plugging the sources into the app research? 
to add, you can ignore build warnings for now, this is not going to be a production application, so no 100% perfectness is required. just to speed things up a bit 

## Prompt 46

writing tests must be delegated to deepseek 

## Prompt 47

meanwhile print me a status report on where we are standing with deliverables 3's task 02, 05, and 09.
to answer your question: print me a command I can execute on arch linux environment to tackle this issue first 

## Prompt 48

02 - once backend tests have run, its done
05 - run a small scoped validation on a few selected sources, after that its done
09 - once 02 and 05 is done, integrate these sources into the backend 

## Prompt 49

I mean (1) integrate the validated news sources after 02 and 05 

## Prompt 50

integration must also be done by deepseek 

## Prompt 51

while openai sol 6 medium limit resets, could you continue the work? 

## Prompt 52

dotnet runtime issue resolved:

":: dt@oamserver-1 (~) :: $ dotnet --list-runtimes
Microsoft.AspNetCore.App 10.0.13 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]
Microsoft.NETCore.App 10.0.13 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
:: dt@oamserver-1 (~) :: $ " 

## Prompt 53

proceed with 06 - integrate all these sources into the backend. once you complete it (don't delegate this to qwen, its way beyond its context capacity), I will proceed with testing it 

## Prompt 54

I mean you can still delgate coding tasks and reseach to qwen, just not huge ones. (small ones). also make sure that all the sources will be integrated. the original plan contained way more sources (coverage matrix if you can recall). 

these were the findings on such sources in our previous research: /prod/sonrisa_homework/docs/requirement-coverage.md /prod/sonrisa_homework/docs/source-integration-research.md 

## Prompt 55

delegate 09 to qwen 

## Prompt 56

excellent job. how could I test 06 by myself quickly?

## Prompt 57

could you launch it, then give me curl commands I could use in my terminal to see some output? (testing) 

## Prompt 58

in regards to qwen's 09 finding: then we need to fall back to yahoo. if that won't work, you need to look for keyless free alternatives on the internet. 

## Prompt 59

in that case if yahoo fails too, let's go with option 3.: "narrow market scope to documented keyless crypto + FX,"
in paralell, can we progress iwht task 07 as well with a separate subagent? 

## Prompt 60

integrate "2. Validated but not yet integrated" into the app. (subagent-1, deepseek)
also integrate "3. Probed, responded, not adopted" into the app as well. (subagent-1, deepseek)
retrieve reliable sources for "5. Still uncovered" and integrate them into the app (qwen) 

## Prompt 61

while these subagents at work, pick up task 03 and start implementing it 

## Prompt 62

I noticed that there are uncommited changes for the done items. commit them. (one commit, one done item).
could you print another coverage matrix plus api request samples? then we can move on with task 14, 15 and 16. 
also a separate subagent should start working on task 18, then another one on setting up infra required for task 20 and 21

## Prompt 63

yeah I understand. don't forget to commit them once it finishes. plus of course "print the new coverage matrix and live request samples." too

## Prompt 64

for task 14, 15 and 16, you should spawn 3 separate agents, let them research and implement the requirements. you must sync their results at the end so their proposals will end up being consistent 
for the scoring algorithm.

for the coverage matrix, I see that there are critical limitations. e.g. we don't receive important news event from the last 24 hours (human readable), or totally obsololete or technically unfitting ones. 
we must eliminate these and find better sources instead of them. keep going until all these bleeding issues are tackled one-by-one. 

I reviewed the api responses, they are promising, but the above limitations I described to you are major issues.

## Prompt 65

meanwhile delegate task 12 to qwen

## Prompt 66

then, delegate task 13 for GPT 6 sol medium

## Prompt 67

I have re-logged into gpt, so it should be fine now

## Prompt 68

I have reached deepseek rate limit, so from now we must rely on gpt 6 sol, openrouter free models and local qwen

## Prompt 69

while these agents at work, make sure to print what is available for testing, on how could I manual test them. or, if you can print runtime test proof, please do so.

## Prompt 70

I see that the obsolete data fetching issue has been resolved. please commit each task's related changesets one by one. what's up with wip tasks?

## Prompt 71

the commited task's status can be set to done.

## Prompt 72

whatsup with 14, 15, 16 and 04?

## Prompt 73

well, that's a problem. wire them into runtime alert selection

## Prompt 74

I will need live samples after thats been done

## Prompt 75

is task 01 closeable?

## Prompt 76

fix 1. and I approve 2

## Prompt 77

many leftovers in the tree left uncommited...

## Prompt 78

commited tasks can be set as done

## Prompt 79

whatsup with the wip ones?

## Prompt 80

please spawn 3 agents to implement the following tasks:

task 26
task 24

plus:

wip tasks must be fully implemented and integrated. once those are done as well, commit them per rule. set task 17 to testing and provide some samples from its live running implementation.

## Prompt 81

there are still many many uncommited changes in the tree

## Prompt 82

right, 17 can be set to done, superior work!

## Prompt 83

launch it so I can test it, to add, use bootstrap css lib to build a simple but modern interface that implements what is required for the view

## Prompt 84

fix those in task 26

## Prompt 85

plus please commit uncommited changes from the git tree. you forgot to commit them along the way. commit = task as defined per global rule
