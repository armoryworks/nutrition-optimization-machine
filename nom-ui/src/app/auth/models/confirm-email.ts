//This is just structural -- these need to be send as querystring parameters to the GET operator
export interface ConfirmEmail {
  //required
  userId: string;
  //required
  code: string;
  //optional
  changedEmail?: string;
}
