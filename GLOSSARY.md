# AppTrack

The candidacies a person has underway, opened from application notices and corrected by that person.

## Language

**Job Application**:
One candidacy for one role at one company. It comes into existence from an inbound email that names the Company and shows the candidacy was submitted.
_Avoid_: Application, submission, lead

**Company**:
The organization a Job Application is with. Every Job Application has a known Company.
_Avoid_: Employer, organization

**Role**:
The position a Job Application is for. The Role is unnamed when that position is not specified or cannot be determined. Its title can be corrected later.
_Avoid_: Job title, position, opening, undetermined

**Application Notice**:
An inbound email that reports the status of exactly one candidacy at a known Company. That status is Application Sent, Interviewing, or Rejected. The Role may be unnamed. It records the time the mail was sent to the person.
_Avoid_: confirmation, job alert, no-reply, job notice

**Sent at**:
The time an Application Notice was sent to the person, taken from the mail's Date header.
_Avoid_: received at, internal date

## Status

**Application Sent**:
The initial status of a Job Application: the candidacy has been submitted.
_Avoid_: Submitted, successful, in review

**Interviewing**:
The status of a Job Application once the candidacy has moved forward to a screen, an interview, or a task.
_Avoid_: In review, in progress, active

**Rejected**:
The status of a Job Application once the employer has ended the candidacy.
_Avoid_: Declined, closed, no
