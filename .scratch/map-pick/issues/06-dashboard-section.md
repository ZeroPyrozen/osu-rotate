# "Map picks" on the Rules page

Status: done

Blocked by: 02

Add a "Map picks" section to the dashboard's Rules page (`RulesPanel.razor`, `RuleForm.cs`) with
an on/off toggle and the oldest ranked year, using the existing controls (`ToggleRow`,
`NumberRow`). The year accepts 2007 to the current year. Saving goes through the same path as the
other rules and takes effect without a restart.

Done when form tests cover loading, saving and an out-of-range year.

## Outcome

A "Map picks" section on the Rules page, between Map regulation and Room: a "!pick finds a map"
switch and "Oldest ranked year" (`RulesPanel.razor`, `RuleForm.PickEnabled` /
`PickOldestRankedYear`). It saves through the same `RuleActions.SaveAsync` as the other rules and
reaches the lobby without a restart.

The year: the lobby refuses outside 2007 to 2100 (issue 02); `RuleActions` also refuses a year
after the current one, from its `TimeProvider`. The number box carries `min`/`max` too, which only
guide the browser's spinner.

Tests in `RuleEditingTests`: the fields round-trip and reach the rules, turning picks on reaches a
running lobby, 2006 and next year are refused with a reason, and the section renders. Not looked at
in a browser: the page needs the owner's osu! login.
