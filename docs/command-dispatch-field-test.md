# Command dispatch field-test notes

This follow-up serializes IEC 61850 control-session open, status reads, and Operate traffic with the per-IED MMS I/O gate. The command row is latched busy on the first click and resolves its owning IED directly.

The change deliberately does not retry commands automatically and does not issue a second Select, SBOw, or Operate sequence.

Field validation should confirm one-click Open/Close operation, positive/negative CommandTermination handling, and process feedback timing while reporting and polling are active.
