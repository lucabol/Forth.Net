: benchme ( xt n -- ) \ executes the word with the execution token "xt" n-times
  dup >r              \ save number of iterations
  0 do dup execute loop \ execute word. word must have a neutral stack effect
  cr r> . ." Iterations." cr \ emit message
;
