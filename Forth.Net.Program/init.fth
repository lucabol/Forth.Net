: ( [char] ) parse drop drop ; immediate
: \ 0 word drop ; immediate

\ Some modified from https://theforth.net/package/minimal/current-view/README.md
: variable create 0 , ;
: constant create , does> @ ;

\ Arithmetic
: 1+ 1 + ;
: 2+ 2 + ;
: 1- 1 - ;
: 2- 2 - ;
: min ( n1 n2 -- n3 )  over over > if swap then drop ;
: max ( n1 n2 -- n3 )  over over < if swap then drop ;
: mod ( n n -- n )  1 swap */mod drop ;
: dec 10 base ! ;
: hex 16 base ! ;
: 2* 2 * ;
: negate -1 * ;
: d- - ;

\ Stack
: rot ( x1 x2 x3 -- x2 x3 x1 )  >r  swap r> swap ;
: -rot ( x1 x2 x3 -- x3 x2 x1 )  rot rot ;
: nip ( x1 x2 -- x2 )  swap drop ;
: tuck ( x1 x2 -- x2 x1 x2 )  swap over ;
: ?dup dup 0 <> if dup then ;
: bounds ( addr1 u -- addr2 addr3 )  over + swap ;
: 2dup ( d1 -- d1 d1 )  over over ;
: 2swap ( d1 d2 -- d2 d1 )  >r rot rot r> rot rot ;
: 2over ( d1 d2 -- d1 d2 d1 )  >r >r 2dup r> r> 2swap ;
: um/mod 2dup mod -rot / ;

\ Boolean
0 constant false
false invert constant true
: 0= 0 = ;
: 0< 0 < ;
: 0> 0 > ;
: or ( x x -- x )  invert swap invert and invert ; ( do morgan )
: xor ( x x -- x )  over over invert and >r swap invert and r> or ;
: lshift ( x1 u -- x2 )  begin dup while >r  2*  r> 1 - repeat drop ;
: endif postpone then ; immediate

\ Memory
: ? @ . ;
: +! ( x addr -- )  swap over @ + swap ! ;
: chars ;
: char+ ( c-addr1 -- c-addr2 )  1 chars + ;
: cell+ ( addr1 -- addr2 )  1 cells + ;
: aligned ( addr -- a-addr )  cell+ 1 -   1 cells 1 - invert  and ;
: 2! ( d addr -- )   SWAP OVER ! CELL+ ! ;
: 2@ ( addr -- d )  DUP CELL+ @ SWAP @ ;

\ Compiler
: ' bl word find drop ;
: ['] ' postpone literal ; immediate
: value ( -- )  create , does> @ ;
: defer ( "<spaces>name" -- )  create 0 , does> @ execute ;
: to ( x "<spaces>name" -- ) 
   state @ 
   if  postpone [']  postpone >body postpone !  
   else ' >body ! then ; immediate

: is ( x "<spaces>name" -- ) 
   state @ if  postpone to  else ['] to execute  then ; immediate

\ Strings
: space ( -- )  bl emit ;
: spaces ( u -- ) dup 0 > if  begin dup while  space 1 -  repeat  then  drop ;

\ .net inteop samples
: .net ( type-s-addr type-c methodName-s-addr method-name-c -- ** )
	2swap .net>type .net>method .net>call ;

: escape s" System.Uri, System" s" EscapeDataString" .net ;
: sqrt s" System.Math" s" Sqrt" .net ;
