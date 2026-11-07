# Requests log

The researcher's requests for changes to the app, word for word, with the commits that delivered them. See the [changelog](../CHANGELOG.md) for what was done.

This is the researcher's side of the conversations with Claude Code, limited to requests about the app. Spelling is as typed, one email address has been removed, and the project's earlier name is replaced by CliniMark. The replies aren't here because they contain passwords and other private details; the study team keeps the complete logs privately.

## 2026-10-31: first round of changes

Delivered in `516ad06` and `f46f78c`.

**2026-10-31 16:10 UTC**

> create a new directory CliniMark_app in projects. I am a contributor to the following github repo https://github.com/ArkhunAbdullazade/CliniMark.git , and I want to be able to push and pull all the changes made to this repo from here. I also want to run and analyze this repo in all details.

**2026-10-31 19:32 UTC**

> I want the following modifications to be made to the app:
>
> 1. replace the name with the new logo which I placed in the CliniMark_app directory. Display it in the same place where the title was. Arrange the size and position nicely, naturally. 
>
> 2. increase the vertical width of the text box, so that it can fully fit on average 44 words. 
>
> 3. replace the text "Draw a rectangle or closed freehand outline around the current term. Drag a shape to move it; use its corner handles to resize, the round handle to rotate, and X to delete. Saved terms can be selected for review." with the following "Draw a rectangular boundary box or a free contour around the term in red. Zoom, move, resize, rotate, and delete as needed. "
>
> 4. Upon clicking on the zoom icon, no need to have "Enlarged area
> N× larger than on the page". 
>
> 5. Increase the horizontal length of two boxes on the right (Metadata box, and comments box) a bit, leave about 0.5 cm from the text box on the left. 
>
> 6. place Change Filter button into a red boundary, to make it more visible. 
>
> 7. all boxes must be with slightly more visible rounded corners (at the moment it is not that visible that they are rounded).
>
> Apply

**2026-10-31 19:51 UTC**

> Further modifications needed:
>
> 1. when A+ and A- are presssed, the size of the text box is changing too. It should not, only the font of the text inside should change, the size of the text box should remain fixed. 
>
> 2. For all the available text descriptions we have right now within CliniMark_app, make sure there is no empty lines between, all text must be in a single paragraph, not several paragraphs like. 
>
> Apply these

**2026-10-31 19:54 UTC**

> 1. upon drawing a boundary box or a contour, make X (delete sign) a bit larger. 
>
> 2. upon drawing a boundary box, and hovering over the top red dot for rotation, make the clockwise rotation sign appearing. Currently no icon changing happens when hovering on a red dot, which make a doctor unclear what it is for. It is for rotation, so rotation sign must apperar.

**2026-10-31 19:58 UTC**

> make the following text in red, and make it inclined (like ctrl + I): "Draw a rectangular boundary box or a free contour around the term in red. Zoom, move, resize, rotate, and delete as needed."

**2026-10-31 20:30 UTC**

> 1. place the "Draw a rectangular boundary box or a free contour around the term in red. Zoom, move, resize, rotate, and delete as needed." above the text description box. 
>
> 2. Change "Rectangle Mode" to "Boundary Box", and "Freehand Mode" to "Freehand Contour" 
>
> 3. increase a bit the size of the tools on the right of the image, at the moment they are a bit small in my opinion, must be a little bit larger
>
> 4. always make bold and in red the medical keyword coming after Annotate the current term: 
>
> 5.  upon clickin on a "Change filters" (change this to "Filters"), add Search and Select/Unselect all functionality for each of the filtering boxes, this must be according to the best practices. Also, add a button "Cancel and Return" next to "Save and Continue" (change to this). Finally, I don't want any descriptive text explaining inside the Filters, I wanna see only the filters themselves as they are now, no need for the following text "
>
>
> Image selections
> These selections apply whenever you open annotation work. Saved annotations stay available for matching images.
>
> Choose images to annotate
> Select at least one option in each list. You can select multiple options. Images must match a selection in all three lists.
>
>
> " 
>
>
>
> 6. Place "Change filters" button in the left corner, but on the same level as three bottons under the image.
>
> 7. place Log Out to the top right, instead of bottom right.

**2026-10-31 20:41 UTC**

> replace "Team progress in selected filters: 1/57" simply with "Progress: 1/57", and place it to the left of Log Out on top right, thereby releasing the bottom area it occupied earlier.

**2026-10-31 20:44 UTC**

> 1. All bottons under the image are way too below, lift them a bit. Also decrease the vertical width of the Comments box

**2026-10-31 20:59 UTC**

> 1. Now buttoms under the image are way to close to the image, leave a bit more space. 
>
> 2. move metadata box a bit down to make it start at the same level as the image text description box. 
>
> 3. add the following animation to each of the buttons on the right of the image: upon hovering on the icon, I want to see an animation which appears a text to the right of what this icon is doing (the same text which appears in grey box when hovered for 2 seconds), text being in White within Red rectangular box which now contains the text and the icon. When not hovered over the icon, only the icon is seen, as it is now. This must apply to all 4 icons next to the image. Let's try

**2026-10-31 21:12 UTC**

> 1. get rid of Magnifier tool, we don't need it any more. 
>
> 2. make the word chunk "term in red" bold and red in "Draw a rectangular boundary box or a free contour for the term in red. Zoom, move, resize, rotate, and delete as needed.". Also increase a little bit the font size of this sentences.

**2026-10-31 21:20 UTC**

> delete the following sentence on top "Draw a rectangular boundary box or a free contour for the term in red. Zoom, move, resize, rotate, and delete as needed." , then save the changes and push this final state of everything to the master branch,

## 2026-10-31 to 2026-11-01: comments, problem reports, prompts

Delivered in `46e5779` and `9642828`.

**2026-10-31 23:25 UTC**

> Change the following: 
>
> 1. currently Comments have a dedicated box which is always there in static. I want it to be more like a toggled button, upon hovering and/or clicking on which a box drops down in the animation, letting a user to type something. Similarly for the next point 2. 
>
> 2. We need a "Report a technical problem", located right next to Comments. Upon clicking on it, a user can type in an animated window what kind of problem they encountered. This window must appear in an animation similar to the Comments box, so that a user can type and see still the full page (namely, it should not open a separate window). Once done, obviously anything typed in Comments or Report a technical problem window must be saved in the database upon moving to the next medical keyword annotation. 
>
> 3. It is important that a medical term is highlighted only once in the text description, even if it appears several times. We need only one set of annotations per medical term, no duplicated work for a physician. 
>
> Now I wanna ask for your opinion on this app, both from the technical and design standpoint. I believe you understood what it is doing and why we developed it. Based on that, what critique and suggestions you have to make it better for clinicians to annotate and us to collect the data? We need to avoid any possible prblem which might prevent clinicinas from accurately doing annotations and us receiving those, alongside other data.

**2026-10-31 23:40 UTC**

> A few more changes: 
>
> 1. buttons to the right of the text box must be in red.
> 2. make text description box a bit larger, with text default font size also increased. 
> 3. "Describe a technical problem" is enough for the description within a window for Reporting a technical problem.

**2026-10-31 23:49 UTC**

> 1. Currently a problem report can't be sent if the page breaks. It can only be sent with a term answer. If the image doesn't load or the tools stop working, it's never sent. Add an image-level "report a problem" that always works, plus automatic logging of image-load and save errors to the admin dashboard.
>
> 2. we should never put any timing on any session, so no "expired session" for a clinciian, they can take their time while providing annotations.

**2026-11-01 00:20 UTC**

> A few more remarks: 
>
> 1. no need for this description "Send it now, or it is saved with your answer for this term."
> 2. change "Annotate the current term: right lateral incisor. Changes are not saved until you select Save and Next Term." to "Annotate the current term: right lateral incisor. Draw a rectangular boundary box or a free contour. Zoom, move, resize, rotate, and delete as needed."
>
> Once these changes are done, commit and push everything, including the original logo file as well

## 2026-11-01 to 2026-11-02: custom annotations and optional filters

Delivered in `b78a770`.

**2026-11-01 21:56 UTC**

> 1. bring buttons under the image a little bit down, not so close to the image, just a bit down. 
>
> 2. place Comment and Report a technical problem buttons next to each other, with the same window animation, just buttons located on the same line next to each other, not one under the other. 
>
> 3. Now an important new feature must be added: between the second and third button on the right of the image, add a new one, called "Custom annotation" with hover description "Additionally annotate and register any medical term not mentioned or nonhighlighted in the image description". Here the idea is that if a user clicks on this button, a contour anotation (like in a second button) must activated and once a user finishes drawing a contour, a small window above the contour must appear, for user to entering the text for what this annotation describes and submit it. A user must be able to register as many such custom annotations as he/she wants. We must register these new custom medical terms and their annotations alongside all other medical terms and annotations made for the image, but probably in a separate column in a database, called "Custom annotations". 
>
> Implement these

**2026-11-01 22:13 UTC**

> 1. fit the text "Report a technical problem" better inside the button, it's not visually appealing right now
>
> 2. For the sentence "Additionally annotate and register any medical term not mentioned or nonhighlighted in the image description" I meant to have it when a user hovers a cursor on a button for 2+ seconds, appearing in the similar way as all other buttons, this sentence being more like a detailed description of what this button does. The name of the button itself is only "Custom annotation"

**2026-11-02 01:48 UTC**

> a few more changes:
>
> 1. we should not make the selection of any of the options within each of three filter types we have required/compulsory, it all must be optional. Namely, a user can maybe only select "microscopy" in modalities, and all microscopy images will be ready for annotations, no need to force a user to select also a speciality and body region. Only if nothing is selected, ask to select at least one option from any of the filters. 
>
> 2. similarly how more detailed explanation appear upo hovering for 3+ seconds on a button for Custom annotation button, I want a similar thing to apply to all other buttons, based on the text we had earlier for such purposes.

**2026-11-02 02:04 UTC**

> 1. bring timing to 2 seconds for appearing box upon hovering 
>
> 2. Here are corrected text for some (keep others as they are):
>
> Zoom to Area: "Drag a box over the image to see it enlarged." 
>
> Not Visible/Abstract: "Click if you are certain that it is not possible to visually annotate the current medical term in this particular image"
>
> Uncertain/Skip: "Click if you are unsure whether you want to annotate this case, or whether the current medical term is present in the image at all, or of its exact location." 
>
> Save and Next Term: Nothing needed here 
>
> A+ / A− / expand: Nothing needed here too 
>
> Submit Annotations (student): Nothing needed here too

## 2026-11-02 to 2026-11-03: deployment

Delivered in `af7e60a` and on Azure.

**2026-11-02 20:57 UTC**

> Here is the situation: we are pretty much done with CliniMark_app and with preparing CliniMarkDatasets. Now we are left with the last stage, which is to deploy this application, load the dataset, and make everything ready for clinicians to start using the app and creating annotations. This is the big picture. Now we need to connect everything that needs to be connected and make everything work smoothly without any problems. I want to plan all of this. In particular, we have a Microsoft Azure account, and I have $200 in credits. Obviously, I want to host the application and the data, which are the medical images, the medical description, and the medical keywords. I want to understand now how to connect and link CliniMarkDatasets (in particular, the polished main data in the combined release directory within the CliniMarkDatasets directory) with the setup of the application we have at the moment.

**2026-11-02 22:47 UTC**

> 1. approved
> 2. a. I will
> b. Yes, both these emails
> c. our clinicians will join from all across the world, including USA, Germany, Austria, Azerbaijan, Turkey, etc. 
>
> 3. a. let's add this option later, not now. 
> b. let's keep the deployment scripts local for now, on my machine. We will create a private ops repo later.

**2026-11-03 00:08 UTC**

> A few remarks:
>
> Phace B: bring back 180 clinical photographs under a image modality label formatted as "Clinicinal photographs", but don't add these to the combined polished main data, keep it only inside NEJM. If we decide to add NEJM data in the future, we will add this one extra image modality to the filter as well. 
>
> Phace C: 1. apply all these changes and push to the master branch directly. I want this to be the final version of the app for now.  2, yes, I am ready. 3. yes, proceed like that

**2026-11-03 01:37 UTC**

> how is it going? 
>
> A few remarks and questions:
> 1. send me an email reminder to [email address removed] to upgrade to pay as you go before  a few days before Dec 2nd. 
> 2. I wanna understand how pricing will work every time we edit the app or data, and deploy changes? 
> 3. Do we have an option to make clinciians register and provide their info about speciality and institute they work, etc? Or we currently have only an option to provide email and password to each clinciian separately?

**2026-11-03 01:54 UTC**

> Accounts: let's keep it sign-up closed for now, we will most probably allow sign ups later, after testing the app. 
> Reminder: forget about it, no need to remind. 
>
> Question for you: 
> 1. can you just tell me what gate password is? why should I go myself into the file and delete the log file after?

## 2026-11-03: filter order and end of series

Delivered in `b1a2d61`. Point 4 is about the dataset, not the app.

**2026-11-03 01:51 UTC**

> 1. reorder filters into Image Modality first, Speciality second, and Body region third. 
> 2. Add the following feature: when a clinician completes all the images within the filters he/she selected, Congratulate - say thank you - and suggest to choose further new filters to annotate more images. Let them set up new filter values and continue annotation new series of images. Our goal is to guide clinicians to annotate as many images as they are capable of. 
> 3. Apply 1 and 2, and Update the app and push changes to master branch. 
> 4. let's further polish our polished_main_data (and importantly, register every time we polish it further, with by what principle we do that) - we will keep only those pairs which have speciality Score ≥ 0.9 (the 67.3%). Once these are filtered out, I wanna further polish it to exclude the following pairs: which fall into Body Regions being 1 and 2.
>  1. Caption has no anatomy word: we searched the article sentences that cite the figure. This is the weakest evidence: 3.9% of pairs. 
> 2. Nothing found anywhere: "Unknown": 7.6% of pairs.
>
> I believe this way we will leave only the pairs which have the highest possible accuracy across all three filters. Correct me if I am wrong and further polishing makes sense

## 2026-11-05: label check and test site

Delivered in `e1a7a47` and on the test site.

**2026-11-05 19:49 UTC**

> I'm thinking about adding the following feature to the CliniMark app. I want to give clinicians a task of evaluating whether the body region, the image modality, and the specialty are chosen correctly. If not, let them correct this so that this particular metadata that we collect is very reliable and correct. We need to think about the easiest way of doing this so that it's fast and simple, pretty much like:
> - Is this body region correct?
> - Is this specialty correct?
> - Is this modality correct? If yes, something like just click and "yes". If not, then either they have to type or have to be given a choice. Also, the second topic is that I want to brainstorm with you on all possible metadata that would be good to have about this. What we have is medical images, text, medical keywords, and hopefully annotations. In addition to these four main units, what kind of data and metadata would be the most meaningful to have in this medical context? We have specialty, body region, and image modality, but I want a full, comprehensive list of any kind of metadata that would be of any use to the doctors in the future or in the research.

**2026-11-05 19:53 UTC**

> what do you need from me, and what are the next steps?
> 1. I see your admin accounts. Create them for me and for our home, please.
> 2. I'm going to test the application now. I am working on one more feature to add, and then I'm going to test it.
> 3. Please remind me whether you have run any tests, what they were, what the results were, or exactly what tests you ran. Have a check, for example, of all the different filters, all the different images, and whether all the tools are working. Also check the logic and whether everything is registered correctly, the annotations and things.
> 4. I will upgrade the subscription closer to December 1 or 2.
> 5. I will do the housekeeping too.
> 6. I will decide about clinicians later, after I'm done with testing.

## 2026-11-06: corrections after testing

Delivered in `8cfb5bc`.

**2026-11-06 02:59 UTC**

> we have tested the test app, here are all the corrections to be made: 
>
> 1. Filter values in the top right box must be in bold RED, not black. 
> 2. currently when a user annotates all the medical keywords, but forgets to verify filter values, it's not clear why the next image is not clickable and what the problem is. We should maybe change the filling color of the filter box on top right, and also change to bold red "confirm the image modality, ...." part in the sentence under the text box, so that we kind of point the user to what is missing. 
> 3. in the logic of how CliniMark operates, one image-text pair gets assigned to a clician at once, and several clinicians can annotate pairs in parallel, but never the same pair (we need one set of annotations per pair from a single clinician). 
> 4. get rid of Research gate access page with it's password
> 5. in bottom right corner, on the same level with other buttons, we should add a new button called "Skip entire image", with description "Skip to the next image if the case is out of your speciality". Upon clicking on this button, thie current case receives on open status so that some other user can annotate it, while the next image case in the list appears for the current user. We don't expect any input (annotations and filter values) from the user on the skipped image case, it starts fresh for some other user. 
> 6. Needless to say, we must collect user inputs on whether assigned filter values for 3 filters are correct or not. We will use this later to compare with original values we had in the metadata and update accordingly if needed. 
> 7. in the admin panel, I want a new category called "Full data", which shows all the data currently collected and stored for already annotated case, like cases being in rows and columns being ALL the data we knew before plus collected from the user (e.g. text description, med keywords, annotation coordinates, submitted values for three filters separately, timing, ... ALL THE DATA WE HAD BEFORE AND ACQUIRED ALONG THE WAY ABOUT THE CASE (IMAGE-TEXT PAIR)". 
> 8. at the moment the logic for collecting annotations are via coordinates. Let's keep it this way, but I also wanna have an alternative way of saving these two: via pixel masks. Basically, as you know, the idea is that every pixel of the image gets assigned a 1 or 0 value depending whether it falls or not within and boundary of the annotation made by the clinician. The reason I need this is because later in the research I will use these anntoations to evaluate the performance of segmentation models on these images, and such evaluatiosn are typically done based on the masks and statistics such as Dice and IoU. Take this into account. 
>
> Let's work on these and I will need to test the performance on the test app again, once you're done. 
> Also, answer me in the end to one quick question: do you save in any file all of the app changes I have suggested to make since we cloned the CliniMark app from the beginning? I mean, do all changes get registered somewhere, so that we can always trace back all the changes we made to the app along the entire way?

## 2026-11-06: going live

Delivered in the release described at the top of the [changelog](../CHANGELOG.md).

**2026-11-06 14:34 UTC**

> 1. yes, add CHANGELOG.md to the repo as well, with our conversation logs. 
> 2. yes, add 3700 pairs to live, I confirm this, full data available to clinicians must be roughly 10K.
> 3. replace "Skip entire image" with "Skip the case", and place it on bottom right corner, at the same level as other buttons, just closer to the corner. 
> 4. "Image modality" filter must be (required), not (optional), others are optional. 
> 5. enable user registration/sign up page, with the following changes:
>  - no need to ask for Clinician experience, you can keep the rest. 
> - in Choose images to annotate, image modality filter must be required, others optional 
>
> make these changes, push to master branch, and we are ready to fully deploy the app. Deploy it, and again share all the credentials for me and Arhun. I will contact clinicians and share the link you provided, so that they can begin the annotation process. Obviously zero out / refresh tests I have done so far, we should start clean. 
>
> One last thing: we need two videos, one a short GIF-like one which we will share within emails, to make it look cool about our app. The second must be a full tutorial video, based on the best standards, which goes step by step over all the parts of the app, zooms in and out when needed, shows all functionality, and if possible of course, with naration with a nice voice in british english (if that's not possible, we can go without any voice, with only a demo video itself).

## 2026-11-07: tutorial video in the app

Delivered in the tutorial video and the first-visit tutorial.

**2026-11-07 UTC**

> Remarks: I like the longer 6-min video you prepared, can we change the following in it:
>  1. voice from female to male british access, with natural human voice (not robotic AI voice),
> 2. no captions needed, only audio voice
>  3. make the video a bit faster, so that it lasts 2-3 mins. Currently it is too slow and lengthy, too long pauses.
> 4. obviously, now that we have a new name for the app and project, replace it accordingly.
> Everything else in the video is really good, all functionality is illustrated clearly and pretty much I don't want anything to be changed, the entire workflow is exactly what I need. Only changes mentioned above are needed.
>
> Once you're finished, store the video both locally inside the app folder (you can push it to masters too as tutorial video), but also on my Azure cloud.
>
> Once the video is ready, add the following in the app itself:
> - the first time any clinician opens a web app after signing up, this tutorial video should appear. Upon starting a video, a user must watch it and then he/she is ready to do annotations.
> - between Progress N/M and Log Out buttons, we must place a new button called Tutorial, upon clicking on which this tutorial video starts, so that users can rewatch it if needed.
