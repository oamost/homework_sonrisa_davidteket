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
