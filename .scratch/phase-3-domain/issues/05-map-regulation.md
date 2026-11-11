# Map regulation

Status: ready-for-agent

Port MapChecker (44 tests): star, length, game mode and ranked status limits, violation count before
skipping the host, and reverting a rejected map. The rule itself is pure and belongs in the domain;
fetching beatmaps belongs to an application port (IBeatmapCatalog) that phase 4 implements.
