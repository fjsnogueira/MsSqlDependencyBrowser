﻿select type_desc, name
  from sys.objects
 where type in ('P')
 order by type_desc, name;

 select type_desc, name
  from sys.objects
 where type in ('TF')
 order by type_desc, name;

 select type_desc, name
  from sys.objects
 where type in ('FN')
 order by type_desc, name;

 select type_desc, name
  from sys.objects
 where type in ('V')
 order by type_desc, name;

 select type_desc, name
  from sys.objects
 where type in ('U')
 order by type_desc, name;