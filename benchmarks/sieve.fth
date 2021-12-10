\ Sieve Benchmark -- the classic Forth benchmark    cas 20101204                   

 include benchme.fth

 8192 CONSTANT SIZE   
 VARIABLE FLAGS  
 0 FLAGS !  
 SIZE ALLOT                         
                                                                                    
 : DO-PRIME                                                                         
   FLAGS SIZE 1 FILL  ( set array )                                                 
   0 ( 0 COUNT ) SIZE 0                                                             
   DO FLAGS I + C@                                                                  
     IF I DUP + 3 + DUP I +                                                         
        BEGIN DUP SIZE <                                                            
        WHILE 0   OVER FLAGS +  C!  OVER +  REPEAT                                  
        DROP DROP 1+                                                                
     THEN                                                                           
 LOOP                                                                               
 . ." Primes" CR ;

' do-prime 100 benchme
