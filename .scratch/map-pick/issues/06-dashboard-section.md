# "Map picks" on the Rules page

Status: ready-for-agent

Blocked by: 02

Add a "Map picks" section to the dashboard's Rules page (`RulesPanel.razor`, `RuleForm.cs`) with
an on/off toggle and the oldest ranked year, using the existing controls (`ToggleRow`,
`NumberRow`). The year accepts 2007 to the current year. Saving goes through the same path as the
other rules and takes effect without a restart.

Done when form tests cover loading, saving and an out-of-range year.
